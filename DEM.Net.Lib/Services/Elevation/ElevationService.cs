﻿using DEM.Net.Lib.Interpolation;
using Microsoft.SqlServer.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DEM.Net.Lib
{
    public class ElevationService : IElevationService
    {
        public const float NO_DATA_OUT = 0;
        private readonly IRasterService _IRasterService;
        public ElevationService(IRasterService rasterService)
        {
            _IRasterService = rasterService;
        }


        public string GetDEMLocalPath(DEMDataSet dataSet)
        {
            return _IRasterService.GetLocalDEMPath(dataSet);
        }

        public void DownloadMissingFiles(DEMDataSet dataSet, BoundingBox bbox = null)
        {
            var report = _IRasterService.GenerateReport(dataSet, bbox);

            DownloadMissingFiles_FromReport(report, dataSet);

        }
        public void DownloadMissingFiles(DEMDataSet dataSet, double lat, double lon)
        {
            var report = _IRasterService.GenerateReportForLocation(dataSet, lat, lon);

            DownloadMissingFiles_FromReport(report, dataSet);

        }
        private void DownloadMissingFiles_FromReport(Dictionary<string, DemFileReport> report, DEMDataSet dataSet)
        {
            // Generate metadata files if missing
            foreach (var file in report.Where(kvp => kvp.Value.IsMetadataGenerated == false && kvp.Value.IsExistingLocally == true).Select(kvp => kvp.Value))
            {
                _IRasterService.GenerateFileMetadata(file.LocalName, dataSet.FileFormat, false, false);
            }
            List<DemFileReport> v_filesToDownload = new List<DemFileReport>(report.Where(kvp => kvp.Value.IsExistingLocally == false).Select(kvp => kvp.Value));

            if (v_filesToDownload.Count == 0)
            {
                Trace.TraceInformation("No missing file(s).");
            }
            else
            {
                Trace.TraceInformation($"Downloading {v_filesToDownload.Count} missing file(s).");

                List<Task> tasks = new List<Task>();


                foreach (var file in v_filesToDownload)
                {
                    tasks.Add(DownloadDEMTile(file.URL, dataSet.FileFormat, file.LocalName));
                }
                try
                {

                    Task.WaitAll(tasks.ToArray());

                    _IRasterService.GenerateDirectoryMetadata(dataSet, false, false);
                    _IRasterService.LoadManifestMetadata(dataSet, true);

                }
                catch (AggregateException ex)
                {
                    Trace.TraceError($"Error downloading missing files. Check internet connection or retry later. {ex.GetInnerMostException().Message}");
                }


            }
        }

        async Task DownloadDEMTile(string url, DEMFileFormat fileFormat, string localFileName)
        {

            // Create directories if not existing
            new FileInfo(localFileName).Directory.Create();

            Trace.TraceInformation($"Downloading file {url}...");

            Uri uri = new Uri(url);
            ServicePoint sp = ServicePointManager.FindServicePoint(uri);
            sp.ConnectionLimit = 50;

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                string requestUrl = url;



                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                var sendTask = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var response = sendTask.EnsureSuccessStatusCode();
                var httpStream = await response.Content.ReadAsStreamAsync();


                using (var fileStream = File.Create(localFileName))
                using (var reader = new StreamReader(httpStream))
                {
                    httpStream.CopyTo(fileStream);
                    fileStream.Flush();
                }
            }


            _IRasterService.GenerateFileMetadata(localFileName, fileFormat, false, false);


        }

        /// <summary>
        /// Extract elevation data along line path
        /// </summary>
        /// <param name="lineWKT"></param>
        /// <returns></returns>
        public List<GeoPoint> GetLineGeometryElevation(string lineWKT, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear)
        {
            SqlGeometry geom = GeometryService.ParseWKTAsGeometry(lineWKT);

            if (geom.STGeometryType().Value == "MultiLineString")
            {
                Trace.TraceWarning("Geometry is a multi line string. Only the longest segment will be processed.");
                geom = geom.Geometries().OrderByDescending(g => g.STNumPoints().Value).First();
            }
            return GetLineGeometryElevation(geom, dataSet, interpolationMode);
        }

        public List<GeoPoint> GetLineGeometryElevation(SqlGeometry lineStringGeometry, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear)
        {
            if (lineStringGeometry == null || lineStringGeometry.IsNull)
                return null;
            if (lineStringGeometry.STGeometryType().Value != "LineString")
            {
                throw new Exception("Geometry must be a linestring");
            }
            if (lineStringGeometry.STSrid.Value != 4326)
            {
                throw new Exception("Geometry SRID must be set to 4326 (WGS 84)");
            }

            BoundingBox bbox = lineStringGeometry.GetBoundingBox();
            List<FileMetadata> tiles = this.GetCoveringFiles(bbox, dataSet);

            // Init interpolator
            IInterpolator interpolator = GetInterpolator(interpolationMode);

            int numPointsSql = lineStringGeometry.STNumPoints().Value;
            var sqlStart = lineStringGeometry.STPointN(1);
            var sqlEnd = lineStringGeometry.STPointN(numPointsSql);
            GeoPoint start = new GeoPoint(sqlStart.STY.Value, sqlStart.STX.Value);
            GeoPoint end = new GeoPoint(sqlEnd.STY.Value, sqlEnd.STX.Value);
            double lengthMeters = start.DistanceTo(end);
            int demResolution = dataSet.ResolutionMeters;
            int totalCapacity = 2 * (int)(lengthMeters / demResolution);

            List<GeoPoint> geoPoints = new List<GeoPoint>(totalCapacity);

            using (RasterFileDictionary adjacentRasters = new RasterFileDictionary())
            {
                bool isFirstSegment = true; // used to return first point only for first segments, for all other segments last point will be returned
                foreach (SqlGeometry segment in lineStringGeometry.Segments())
                {
                    List<FileMetadata> segTiles = this.GetCoveringFiles(segment.GetBoundingBox(), dataSet, tiles);

                    // Find all intersection with segment and DEM grid
                    IEnumerable<GeoPoint> intersections = this.FindSegmentIntersections(segment.STStartPoint().STX.Value
                        , segment.STStartPoint().STY.Value
                        , segment.STEndPoint().STX.Value
                        , segment.STEndPoint().STY.Value
                        , segTiles
                        , isFirstSegment
                        , true);

                    // Get elevation for each point
                    this.GetElevationData(ref intersections, dataSet, adjacentRasters, segTiles, interpolator);

                    // Add to output list
                    geoPoints.AddRange(intersections);

                    isFirstSegment = false;
                }
                //Debug.WriteLine(adjacentRasters.Count);
            }  // Ensures all rasters are properly closed

            return geoPoints;
        }
        public List<GeoPoint> GetLineGeometryElevation(IEnumerable<GeoPoint> lineGeoPoints, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear)
        {
            if (lineGeoPoints == null)
                throw new ArgumentNullException("lineGeoPoints", "Point list is null");

            SqlGeometry geometry = GeometryService.ParseGeoPointAsGeometryLine(lineGeoPoints);

            return GetLineGeometryElevation(geometry, dataSet, interpolationMode);
        }

        public GeoPoint GetPointElevation(double lat, double lon, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear)
        {
            GeoPoint geoPoint = new GeoPoint(lat, lon);
            List<FileMetadata> tiles = this.GetCoveringFiles(lat, lon, dataSet);

            // Init interpolator
            IInterpolator interpolator = GetInterpolator(interpolationMode);

            List<GeoPoint> geoPoints = new List<GeoPoint>();

            using (RasterFileDictionary adjacentRasters = new RasterFileDictionary())
            {
                PopulateRasterFileDictionary(adjacentRasters, tiles.First(), _IRasterService, tiles);

                geoPoint.Elevation = GetElevationAtPoint(adjacentRasters, tiles.First(), lat, lon, 0, interpolator);


                //Debug.WriteLine(adjacentRasters.Count);
            }  // Ensures all geotifs are properly closed

            return geoPoint;
        }
        public IEnumerable<GeoPoint> GetPointsElevation(IEnumerable<GeoPoint> points, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear)
        {
            if (points == null )
                return null;
           
            BoundingBox bbox = points.GetBoundingBox();
            DownloadMissingFiles(dataSet, bbox);
            List<FileMetadata> tiles = this.GetCoveringFiles(bbox, dataSet);

            // Init interpolator
            IInterpolator interpolator = GetInterpolator(interpolationMode);

            using (RasterFileDictionary adjacentRasters = new RasterFileDictionary())
            {
               
                    // Get elevation for each point
                    this.GetElevationData(ref points, dataSet, adjacentRasters, tiles, interpolator);

                //Debug.WriteLine(adjacentRasters.Count);
            }  // Ensures all rasters are properly closed

            return points;
        }

        public IInterpolator GetInterpolator(InterpolationMode interpolationMode)
        {
            switch (interpolationMode)
            {
                case InterpolationMode.Hyperbolic:
                    return new HyperbolicInterpolator();
                case InterpolationMode.Bilinear:
                    return new BilinearInterpolator();
                default:
                    throw new NotImplementedException($"Interpolator {interpolationMode} is not implemented.");
            }
        }

        public string ExportElevationTable(List<GeoPoint> lineElevationData)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Lon\tLat\tDistance (meters)\tZ");
            GeoPoint refPoint = lineElevationData.First();
            foreach (GeoPoint pt in lineElevationData)
            {
                sb.AppendLine($"{pt.Longitude.ToString(CultureInfo.InvariantCulture)}\t{pt.Latitude.ToString(CultureInfo.InvariantCulture)}\t{pt.DistanceFromOriginMeters.ToString("F2")}\t{pt.Elevation}");
            }
            return sb.ToString();
        }

        public HeightMap GetHeightMap(BoundingBox bbox, DEMDataSet dataSet)
        {
            DownloadMissingFiles(dataSet, bbox);

            // Locate which files are needed
            // Find files matching coords
            List<FileMetadata> bboxMetadata = GetCoveringFiles(bbox, dataSet);

            // get height map for each file at bbox
            List<HeightMap> tilesHeightMap = new List<HeightMap>();
            foreach (FileMetadata metadata in bboxMetadata)
            {
                using (IRasterFile raster = _IRasterService.OpenFile(metadata.Filename, dataSet.FileFormat))
                {
                    tilesHeightMap.Add(raster.GetHeightMapInBBox(bbox, metadata, NO_DATA_OUT));
                }
            }



            // Merge height maps
            int totalHeight = tilesHeightMap.GroupBy(h => h.BoundingBox.xMin).Select(g => g.Sum(v => v.Height)).First();
            int totalWidth = tilesHeightMap.GroupBy(h => h.BoundingBox.yMin).Select(g => g.Sum(v => v.Width)).First();

            HeightMap heightMap = new HeightMap(totalWidth, totalHeight);
            heightMap.BoundingBox = new BoundingBox(xmin: tilesHeightMap.Min(h => h.BoundingBox.xMin)
                                                    , xmax: tilesHeightMap.Max(h => h.BoundingBox.xMax)
                                                    , ymin: tilesHeightMap.Min(h => h.BoundingBox.yMin)
                                                    , ymax: tilesHeightMap.Max(h => h.BoundingBox.yMax));
            heightMap.Coordinates = tilesHeightMap.SelectMany(hmap => hmap.Coordinates).Sort();
            heightMap.Count = totalWidth * totalHeight;
            heightMap.Mininum = tilesHeightMap.Min(hmap => hmap.Mininum);
            heightMap.Maximum = tilesHeightMap.Min(hmap => hmap.Maximum);

            Debug.Assert(heightMap.Count == tilesHeightMap.Sum(h => h.Count));


            return heightMap;
        }
        public HeightMap GetHeightMap(FileMetadata metadata)
        {
            HeightMap map = null;
            using (IRasterFile raster = _IRasterService.OpenFile(metadata.Filename, metadata.fileFormat))
            {
                map = raster.GetHeightMap(metadata);
            }
            return map;
        }


        /// <summary>
        /// Fill altitudes for each GeoPoint provided, opening as few rasters as possible
        /// </summary>
        /// <param name="intersections"></param>
        /// <param name="segTiles"></param>
        public void GetElevationData(ref IEnumerable<GeoPoint> intersections, DEMDataSet dataSet, RasterFileDictionary adjacentRasters, List<FileMetadata> segTiles, IInterpolator interpolator)
        {
            // Group by raster file for sequential and faster access
            var pointsByTileQuery = from point in intersections
                                    let pointTile = new
                                    {
                                        Point = point,
                                        Tile = segTiles.FirstOrDefault(t => this.IsPointInTile(t, point)),
                                        AdjacentTiles = segTiles.Where(t => this.IsPointInAdjacentTile(t, point)).ToList()
                                    }
                                    group pointTile by pointTile.Tile into pointsByTile
                                    where pointsByTile.Key != null
                                    select pointsByTile;


            try
            {
                float lastElevation = 0;

                // To interpolate well points close to tile edges, we need all adjacent tiles
                //using (RasterFileDictionary adjacentRasters = new RasterFileDictionary())
                //{
                // For each group (key = tile, values = points within this tile)
                // TIP: test use of Parallel (warning : a lot of files may be opened at the same time)
                foreach (var tilePoints in pointsByTileQuery)
                {
                    // Get the tile
                    FileMetadata mainTile = tilePoints.Key;


                    // We open rasters first, then we iterate
                    PopulateRasterFileDictionary(adjacentRasters, mainTile, _IRasterService, tilePoints.SelectMany(tp => tp.AdjacentTiles));


                    foreach (var pointile in tilePoints)
                    {
                        GeoPoint current = pointile.Point;
                        lastElevation = this.GetElevationAtPoint(adjacentRasters, mainTile, current.Latitude, current.Longitude, lastElevation, interpolator);
                        current.Elevation = lastElevation;
                    }

                    //adjacentRasters.Clear();

                }
            }
            //}

            catch (Exception e)
            {
                Trace.TraceError($"Error while getting elevation data : {e.Message}{Environment.NewLine}{e.ToString()}");
            }

        }

        private void PopulateRasterFileDictionary(RasterFileDictionary dictionary, FileMetadata mainTile, IRasterService rasterService, IEnumerable<FileMetadata> fileMetadataList)
        {
            // Add main tile
            if (!dictionary.ContainsKey(mainTile))
            {
                dictionary[mainTile] = rasterService.OpenFile(mainTile.Filename, mainTile.fileFormat);
            }

            foreach (var fileMetadata in fileMetadataList)
            {
                if (!dictionary.ContainsKey(fileMetadata))
                {
                    dictionary[fileMetadata] = rasterService.OpenFile(fileMetadata.Filename, fileMetadata.fileFormat);
                }
            }
        }

        /// <summary>
        /// Finds all intersections between given segment and DEM grid
        /// </summary>
        /// <param name="startLon">Segment start longitude</param>
        /// <param name="startLat">Segment start latitude</param>
        /// <param name="endLon">Segment end longitude</param>
        /// <param name="endLat">Segment end latitude</param>
        /// <param name="segTiles">Metadata files <see cref="RasterService.GetCoveringFiles"/> to see how to get them relative to segment geometry</param>
        /// <param name="returnStartPoint">If true, the segment starting point will be returned. Useful when processing a line segment by segment.</param>
        /// <param name="returnEndPoind">If true, the segment end point will be returned. Useful when processing a line segment by segment.</param>
        /// <returns></returns>
        public List<GeoPoint> FindSegmentIntersections(double startLon, double startLat, double endLon, double endLat, List<FileMetadata> segTiles, bool returnStartPoint, bool returnEndPoind)
        {
            List<GeoPoint> segmentPointsWithDEMPoints;
            // Find intersections with north/south lines, 
            // starting form segment western point to easternmost point
            GeoPoint westernSegPoint = startLon < endLon ? new GeoPoint(startLat, startLon) : new GeoPoint(endLat, endLon);
            GeoPoint easternSegPoint = startLon > endLon ? new GeoPoint(startLat, startLon) : new GeoPoint(endLat, endLon);
            GeoSegment inputSegment = new GeoSegment(westernSegPoint, easternSegPoint);

            if (segTiles.Any())
            {
                int estimatedCapacity = (segTiles.Select(t => t.OriginLongitude).Distinct().Count() // num horizontal tiles * width
                                        * segTiles.First().Width)
                                        + (segTiles.Select(t => t.OriginLatitude).Distinct().Count() // num vertical tiles * height
                                        * segTiles.First().Height);
                segmentPointsWithDEMPoints = new List<GeoPoint>(estimatedCapacity);
                bool yAxisDown = segTiles.First().pixelSizeY < 0;
                if (yAxisDown == false)
                {
                    throw new NotImplementedException("DEM with y axis upwards not supported.");
                }

                foreach (GeoSegment demSegment in this.GetDEMNorthSouthLines(segTiles, westernSegPoint, easternSegPoint))
                {
                    GeoPoint intersectionPoint = null;
                    if (GeometryService.LineLineIntersection(out intersectionPoint, inputSegment, demSegment))
                    {
                        segmentPointsWithDEMPoints.Add(intersectionPoint);
                    }
                }

                // Find intersections with west/east lines, 
                // starting form segment northernmost point to southernmost point
                GeoPoint northernSegPoint = startLat > endLat ? new GeoPoint(startLat, startLon) : new GeoPoint(endLat, endLon);
                GeoPoint southernSegPoint = startLat < endLat ? new GeoPoint(startLat, startLon) : new GeoPoint(endLat, endLon);
                inputSegment = new GeoSegment(northernSegPoint, southernSegPoint);
                foreach (GeoSegment demSegment in this.GetDEMWestEastLines(segTiles, northernSegPoint, southernSegPoint))
                {
                    GeoPoint intersectionPoint = null;
                    if (GeometryService.LineLineIntersection(out intersectionPoint, inputSegment, demSegment))
                    {
                        segmentPointsWithDEMPoints.Add(intersectionPoint);
                    }
                }
            }
            else
            {
                // No DEM coverage
                segmentPointsWithDEMPoints = new List<GeoPoint>(2);
            }

            // add start and/or end point
            if (returnStartPoint)
            {
                segmentPointsWithDEMPoints.Add(inputSegment.Start);
            }
            if (returnEndPoind)
            {
                segmentPointsWithDEMPoints.Add(inputSegment.End);
            }

            // sort points in segment order
            //
            segmentPointsWithDEMPoints.Sort(new DistanceFromPointComparer(new GeoPoint(startLat, startLon)));

            return segmentPointsWithDEMPoints;
        }


        public IEnumerable<GeoSegment> GetDEMNorthSouthLines(List<FileMetadata> segTiles, GeoPoint westernSegPoint, GeoPoint easternSegPoint)
        {
            // Get the first north west tile and last south east tile. 
            // The lines are bounded by those tiles

            foreach (var tilesByX in segTiles.GroupBy(t => t.StartLon).OrderBy(g => g.Key))
            {
                List<FileMetadata> NSTilesOrdered = tilesByX.OrderByDescending(t => t.StartLat).ToList();

                FileMetadata top = NSTilesOrdered.First();
                FileMetadata bottom = NSTilesOrdered.Last();

                // TIP: can optimize here starting with min(westernSegPoint, startlon) but careful !
                GeoPoint curPoint = new GeoPoint(top.StartLat, top.StartLon);

                // X Index in tile coords
                int curIndex = (int)Math.Ceiling((curPoint.Longitude - top.StartLon) / top.PixelScaleX);
                while (IsPointInTile(top, curPoint))
                {
                    if (curIndex >= top.Width)
                    {
                        break;
                    }

                    curPoint.Longitude = top.StartLon + (top.pixelSizeX * curIndex);
                    if (curPoint.Longitude > easternSegPoint.Longitude)
                    {
                        break;
                    }
                    GeoSegment line = new GeoSegment(new GeoPoint(top.OriginLatitude, curPoint.Longitude), new GeoPoint(bottom.EndLatitude, curPoint.Longitude));
                    curIndex++;
                    yield return line;
                }
            }
        }

        public IEnumerable<GeoSegment> GetDEMWestEastLines(List<FileMetadata> segTiles, GeoPoint northernSegPoint, GeoPoint southernSegPoint)
        {
            // Get the first north west tile and last south east tile. 
            // The lines are bounded by those tiles

            foreach (var tilesByY in segTiles.GroupBy(t => t.StartLat).OrderByDescending(g => g.Key))
            {
                List<FileMetadata> WETilesOrdered = tilesByY.OrderBy(t => t.StartLon).ToList();

                FileMetadata left = WETilesOrdered.First();
                FileMetadata right = WETilesOrdered.Last();

                GeoPoint curPoint = new GeoPoint(left.StartLat, left.StartLon);

                // Y Index in tile coords
                int curIndex = (int)Math.Ceiling((left.StartLat - curPoint.Latitude) / left.PixelScaleY);
                while (IsPointInTile(left, curPoint))
                {
                    if (curIndex >= left.Height)
                    {
                        break;
                    }

                    curPoint.Latitude = left.StartLat + (left.pixelSizeY * curIndex);
                    if (curPoint.Latitude < southernSegPoint.Latitude)
                    {
                        break;
                    }
                    GeoSegment line = new GeoSegment(new GeoPoint(curPoint.Latitude, left.OriginLongitude), new GeoPoint(curPoint.Latitude, right.EndLongitude));
                    curIndex++;
                    yield return line;
                }
            }

        }



        public BoundingBox GetTilesBoundingBox(List<FileMetadata> tiles)
        {
            double xmin = tiles.Min(t => t.OriginLongitude);
            double xmax = tiles.Max(t => t.EndLongitude);
            double ymin = tiles.Min(t => t.EndLatitude);
            double ymax = tiles.Max(t => t.OriginLatitude);
            return new BoundingBox(xmin, xmax, ymin, ymax);
        }
        public BoundingBox GetTileBoundingBox(FileMetadata tile)
        {
            double xmin = tile.OriginLongitude;
            double xmax = tile.EndLongitude;
            double ymin = tile.EndLatitude;
            double ymax = tile.OriginLatitude;
            return new BoundingBox(xmin, xmax, ymin, ymax);
        }

        public List<FileMetadata> GetCoveringFiles(BoundingBox bbox, DEMDataSet dataSet, List<FileMetadata> subSet = null)
        {
            // Locate which files are needed

            // Load metadata catalog
            List<FileMetadata> metadataCatalog = subSet ?? _IRasterService.LoadManifestMetadata(dataSet, false);

            // Find files matching coords
            List<FileMetadata> bboxMetadata = new List<FileMetadata>(metadataCatalog.Where(m => IsBboxIntersectingTile(m, bbox)));

            if (bboxMetadata.Count == 0)
            {
                Trace.TraceWarning($"No coverage found matching provided bounding box { bbox}.");
                //throw new Exception($"No coverage found matching provided bounding box {bbox}.");
            }

            return bboxMetadata;
        }
        public List<FileMetadata> GetCoveringFiles(double lat, double lon, DEMDataSet dataSet, List<FileMetadata> subSet = null)
        {
            // Locate which files are needed

            // Load metadata catalog
            List<FileMetadata> metadataCatalog = subSet ?? _IRasterService.LoadManifestMetadata(dataSet, false);

            var geoPoint = new GeoPoint(lat, lon);
            // Find files matching coords
            List<FileMetadata> bboxMetadata = new List<FileMetadata>(metadataCatalog.Where(m => IsPointInTile(m, geoPoint)));

            if (bboxMetadata.Count == 0)
            {
                Trace.TraceWarning($"No coverage found matching provided point {geoPoint}.");
                //throw new Exception($"No coverage found matching provided bounding box {bbox}.");
            }

            return bboxMetadata;
        }

        public bool IsBboxIntersectingTile(FileMetadata tileMetadata, BoundingBox bbox)
        {
            BoundingBox tileBBox = GetTileBoundingBox(tileMetadata);

            return (tileBBox.xMax >= bbox.xMin && tileBBox.xMin <= bbox.xMax) && (tileBBox.yMax >= bbox.yMin && tileBBox.yMin <= bbox.yMax);
        }
        public bool IsPointInTile(FileMetadata tileMetadata, GeoPoint point)
        {
            BoundingBox bbox = GetTileBoundingBox(tileMetadata);

            bool isInsideY = bbox.yMin <= point.Latitude && point.Latitude <= bbox.yMax;
            bool isInsideX = bbox.xMin <= point.Longitude && point.Longitude <= bbox.xMax;
            bool isInside = isInsideX && isInsideY;
            return isInside;
        }
        private bool IsPointInAdjacentTile(FileMetadata tile, GeoPoint point)
        {
            BoundingBox tileBbox = GetTileBoundingBox(tile);
            double sX = tile.PixelScaleX * 2;
            double sY = tile.PixelScaleY * 2;
            bool isInsideY = (tileBbox.yMin - sY) <= point.Latitude && point.Latitude <= (tileBbox.yMax + sY);
            bool isInsideX = (tileBbox.xMin - sX) <= point.Longitude && point.Longitude <= (tileBbox.xMax + sX);
            bool isInside = isInsideX && isInsideY;
            return isInside;
        }

        public float GetElevationAtPoint(RasterFileDictionary adjacentTiles, FileMetadata metadata, double lat, double lon, float lastElevation, IInterpolator interpolator)
        {
            float heightValue = 0;
            try
            {

                IRasterFile mainRaster = adjacentTiles[metadata];

                //const double epsilon = (Double.Epsilon * 100);
                float noData = metadata.NoDataValueFloat;


                // precise position on the grid (with commas)
                double ypos = (lat - metadata.StartLat) / metadata.pixelSizeY;
                double xpos = (lon - metadata.StartLon) / metadata.pixelSizeX;

                // If pure integers, then it's on the grid
                float xInterpolationAmount = (float)xpos % 1;
                float yInterpolationAmount = (float)ypos % 1;

                bool xOnGrid = xInterpolationAmount == 0;
                bool yOnGrid = yInterpolationAmount == 0;

                // If xOnGrid and yOnGrid, we are on a grid intersection, and that's all
                if (xOnGrid && yOnGrid)
                {
                    int x = (int)Math.Round(xpos, 0);
                    int y = (int)Math.Round(ypos, 0);
                    var tile = FindTile(metadata, adjacentTiles, x, y, out x, out y);
                    heightValue = mainRaster.GetElevationAtPoint(tile, x, y);
                }
                else
                {
                    int xCeiling = (int)Math.Ceiling(xpos);
                    int xFloor = (int)Math.Floor(xpos);
                    int yCeiling = (int)Math.Ceiling(ypos);
                    int yFloor = (int)Math.Floor(ypos);
                    // Get 4 grid nearest points (DEM grid corners)

                    // If not yOnGrid and not xOnGrid we are on grid horizontal line
                    // We need elevations for top, bottom, left and right grid points (along x axis and y axis)
                    float northWest = GetElevationAtPoint(metadata, adjacentTiles, xFloor, yFloor, NO_DATA_OUT);
                    float northEast = GetElevationAtPoint(metadata, adjacentTiles, xCeiling, yFloor, NO_DATA_OUT);
                    float southWest = GetElevationAtPoint(metadata, adjacentTiles, xFloor, yCeiling, NO_DATA_OUT);
                    float southEast = GetElevationAtPoint(metadata, adjacentTiles, xCeiling, yCeiling, NO_DATA_OUT);

                    float avgHeight = GetAverageExceptForNoDataValue(noData, NO_DATA_OUT, southWest, southEast, northWest, northEast);

                    if (northWest == noData) northWest = avgHeight;
                    if (northEast == noData) northEast = avgHeight;
                    if (southWest == noData) southWest = avgHeight;
                    if (southEast == noData) southEast = avgHeight;

                    heightValue = interpolator.Interpolate(southWest, southEast, northWest, northEast, xInterpolationAmount, yInterpolationAmount);
                }

                if (heightValue == NO_DATA_OUT)
                {
                    heightValue = lastElevation;
                }
            }
            catch (Exception e)
            {
                Trace.TraceError($"Error while getting elevation data : {e.Message}{Environment.NewLine}{e.ToString()}");
            }
            return heightValue;
        }

        private float GetElevationAtPoint(FileMetadata mainTile, RasterFileDictionary tiles, int x, int y, float nullValue)
        {
            int xRemap, yRemap;
            FileMetadata goodTile = FindTile(mainTile, tiles, x, y, out xRemap, out yRemap);
            if (goodTile == null)
            {
                return nullValue;
            }

            if (tiles.ContainsKey(goodTile))
            {
                return tiles[goodTile].GetElevationAtPoint(goodTile, xRemap, yRemap);

            }
            else
            {
                throw new Exception("Tile not found. Should not happen.");
            }

        }

        private FileMetadata FindTile(FileMetadata mainTile, RasterFileDictionary tiles, int x, int y, out int newX, out int newY)
        {
            int xTileOffset = x < 0 ? -1 : x >= mainTile.Width ? 1 : 0;
            int yTileOffset = y < 0 ? -1 : y >= mainTile.Height ? 1 : 0;
            if (xTileOffset == 0 && yTileOffset == 0)
            {
                newX = x;
                newY = y;
                return mainTile;
            }
            else
            {
                int yScale = Math.Sign(mainTile.pixelSizeY);
                FileMetadata tile = tiles.Keys.FirstOrDefault(
                    t => t.OriginLatitude == mainTile.OriginLatitude + yScale * yTileOffset
                    && t.OriginLongitude == mainTile.OriginLongitude + xTileOffset);
                newX = xTileOffset > 0 ? x % mainTile.Width : (mainTile.Width + x) % mainTile.Width;
                newY = yTileOffset < 0 ? (mainTile.Height + y) % mainTile.Height : y % mainTile.Height;
                return tile;
            }
        }




        public float GetAverageExceptForNoDataValue(float noData, float valueIfAllBad, params float[] values)
        {
            var withValues = values.Where(v => v != noData);
            if (withValues.Any())
            {
                return withValues.Average();
            }
            else
            {
                return valueIfAllBad;
            }
        }




    }
}