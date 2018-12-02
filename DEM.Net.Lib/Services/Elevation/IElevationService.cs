﻿using System.Collections.Generic;
using Microsoft.SqlServer.Types;

namespace DEM.Net.Lib
{
    public interface IElevationService
    {
        /// <summary>
        /// Given a bounding box and a dataset, downloads all covered tiles
        /// using VRT file specified in dataset
        /// </summary>
        /// <param name="dataSet">DEMDataSet used</param>
        /// <param name="bbox">Bounding box, <see cref="GeometryService.GetBoundingBox(string)"/></param>
        /// <remarks>VRT file is downloaded once. It will be cached in local for 30 days.
        /// </remarks>
        void DownloadMissingFiles(DEMDataSet dataSet, BoundingBox bbox = null);

        /// <summary>
        /// Given a bounding box and a dataset, downloads all covered tiles
        /// using VRT file specified in dataset
        /// </summary>
        /// <param name="dataSet">DEMDataSet used</param>
        /// <param name="lat">Latitude of location</param>
        /// <param name="lon">Longitude of location</param>
        /// <remarks>VRT file is downloaded once. It will be cached in local for 30 days.
        /// </remarks>
        void DownloadMissingFiles(DEMDataSet dataSet, double lat, double lon);

        /// <summary>
        /// High level method that retrieves all dataset elevations along given line
        /// </summary>
        /// <param name="lineGeoPoints">List of points that, when joined, makes the input line</param>
        /// <param name="dataSet">DEM dataset to use</param>
        /// <param name="interpolationMode">Interpolation mode</param>
        /// <remarks>Output can be BIG, as all elevations will be returned.</remarks>
        /// <returns></returns>
        List<GeoPoint> GetLineGeometryElevation(IEnumerable<GeoPoint> lineGeoPoints, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear);
        /// <summary>
        /// High level method that retrieves all dataset elevations along given line
        /// </summary>
        /// <param name="lineStringGeometry">Line geometry</param>
        /// <param name="dataSet">DEM dataset to use</param>
        /// <param name="interpolationMode">Interpolation mode</param>
        /// <remarks>Output can be BIG, as all elevations will be returned.</remarks>
        /// <returns></returns>
        List<GeoPoint> GetLineGeometryElevation(SqlGeometry lineStringGeometry, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear);
        /// <summary>
        /// High level method that retrieves all dataset elevations along given line
        /// </summary>
        /// <param name="lineWKT">Line geometry in WKT</param>
        /// <param name="dataSet">DEM dataset to use</param>
        /// <param name="interpolationMode">Interpolation mode</param>
        /// <remarks>Output can be BIG, as all elevations will be returned.</remarks>
        /// <returns></returns>
        List<GeoPoint> GetLineGeometryElevation(string lineWKT, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear);
        /// <summary>
        /// High level method that retrieves elevation for given point
        /// </summary>
        /// <param name="lat">Point latitude</param>
        /// <param name="lon">Point longitude</param>
        /// <param name="dataSet">DEM dataset to use</param>
        /// <param name="interpolationMode">Interpolation mode</param>
        /// <returns></returns>
        GeoPoint GetPointElevation(double lat, double lon, DEMDataSet dataSet, InterpolationMode interpolationMode = InterpolationMode.Bilinear);

        /// <summary>
        /// Returns all elevations in given bbox
        /// </summary>
        /// <param name="bbox"></param>
        /// <param name="dataSet"></param>
        /// <returns></returns>
        HeightMap GetHeightMap(BoundingBox bbox, DEMDataSet dataSet);

        /// <summary>
        /// Get all elevation for a given raster file
        /// </summary>
        /// <param name="metadata">Raster file metadata. <see cref="GetCoveringFiles(BoundingBox, DEMDataSet, List{FileMetadata})"></see></param>
        /// <returns></returns>
        HeightMap GetHeightMap(FileMetadata metadata);

        /// <summary>
        /// Retrieves bounding box of given raster file
        /// </summary>
        /// <param name="tile"></param>
        /// <returns></returns>
        BoundingBox GetTileBoundingBox(FileMetadata tile);
        /// <summary>
        /// Retrieves bounding box for the uning of all raster file list
        /// </summary>
        /// <param name="tiles"></param>
        /// <returns></returns>
        BoundingBox GetTilesBoundingBox(List<FileMetadata> tiles);
        /// <summary>
        /// Performs point / bbox intersection
        /// </summary>
        /// <param name="originLatitude"></param>
        /// <param name="originLongitude"></param>
        /// <param name="bbox"></param>
        /// <returns></returns>
        bool IsBboxIntersectingTile(FileMetadata tileMetadata, BoundingBox bbox);
        bool IsPointInTile(FileMetadata tileMetadata, GeoPoint point);
        List<FileMetadata> GetCoveringFiles(BoundingBox bbox, DEMDataSet dataSet, List<FileMetadata> subSet = null);
        List<FileMetadata> GetCoveringFiles(double lat, double lon, DEMDataSet dataSet, List<FileMetadata> subSet = null);
        string GetDEMLocalPath(DEMDataSet dataSet);

      

        
        List<GeoPoint> FindSegmentIntersections(double startLon, double startLat, double endLon, double endLat, List<FileMetadata> segTiles, bool returnStartPoint, bool returnEndPoind);
        float GetAverageExceptForNoDataValue(float noData, float valueIfAllBad, params float[] values);
        
        IEnumerable<GeoSegment> GetDEMNorthSouthLines(List<FileMetadata> segTiles, GeoPoint westernSegPoint, GeoPoint easternSegPoint);
        IEnumerable<GeoSegment> GetDEMWestEastLines(List<FileMetadata> segTiles, GeoPoint northernSegPoint, GeoPoint southernSegPoint);
        float GetElevationAtPoint(RasterFileDictionary adjacentTiles, FileMetadata metadata, double lat, double lon, float lastElevation, IInterpolator interpolator);
        void GetElevationData(ref List<GeoPoint> intersections, DEMDataSet dataSet, RasterFileDictionary adjacentRasters, List<FileMetadata> segTiles, IInterpolator interpolator);
       
        IInterpolator GetInterpolator(InterpolationMode interpolationMode);
       

        /// <summary>
        /// Generate a tab separated list of points and elevations
        /// </summary>
        /// <param name="lineElevationData"></param>
        /// <returns></returns>
        string ExportElevationTable(List<GeoPoint> lineElevationData);
    }
}