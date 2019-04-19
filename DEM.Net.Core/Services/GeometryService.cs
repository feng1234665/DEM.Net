﻿// GeometryService.cs
//
// Author:
//       Xavier Fischer
//
// Copyright (c) 2019 
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the right
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoAPI.Geometries;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DEM.Net.Core
{
    /// <summary>
    /// Geometry related services 
    /// </summary>
	public static class GeometryService
	{
		private const int WGS84_SRID = 4326;
		private const double EARTH_RADIUS = 6371008.0; // [m]
		private const double RADIAN = Math.PI / 180;

        private static WKTReader _wktReader;

        static GeometryService()
        {
            _wktReader = new WKTReader(GeometryFactory.Default);
        }

        /// <summary>
        /// Translates a geometry WKT string to a NetTopology geometry
        /// </summary>
        /// <param name="geomWKT">Geometry Well Known Text</param>
        /// <param name="srid">SRID of geomtery (defaults to 4326)</param>
        /// <returns>NetTopology IGeometry instance</returns>
        public static IGeometry ParseWKTAsGeometry(string geomWKT, int srid = WGS84_SRID)
        {
            IGeometry geometry = _wktReader.Read(geomWKT);
            geometry.SRID = srid;
            return geometry;
        }
        /// <summary>
        /// Gets the bounding box (envelope) of a given geometry
        /// </summary>
        /// <param name="geomWKT">Geometry Well Known Text</param>
        /// <returns><see cref="BoundingBox"/></returns>
        public static BoundingBox GetBoundingBox(string geomWKT)
        {
            IGeometry geom = ParseWKTAsGeometry(geomWKT);
            return geom.GetBoundingBox();
        }
        /// <summary>
        /// Extension method. Returns the bounding box for a geometry instance
        /// </summary>
        /// <param name="geom">NetTopology IGeometry instance</param>
        /// <returns></returns>
        public static BoundingBox GetBoundingBox(this IGeometry geom)
        {
            Envelope envelope = geom.EnvelopeInternal;

            return new BoundingBox(envelope.MinX, envelope.MaxX, envelope.MinY, envelope.MaxY);
        }


        /// <summary>
        /// Returns the bounding box of a set of points
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static BoundingBox GetBoundingBox(this IEnumerable<GeoPoint> points)
        {
            BoundingBox bbox = new BoundingBox(double.MaxValue, double.MinValue, double.MaxValue, double.MinValue);

            foreach (var pt in points)
            {
                bbox.xMin = Math.Min(bbox.xMin, pt.Longitude);
                bbox.xMax = Math.Max(bbox.xMax, pt.Longitude);

                bbox.yMin = Math.Min(bbox.yMin, pt.Latitude);
                bbox.yMax = Math.Max(bbox.yMax, pt.Latitude);
            }
            return bbox;
        }
        /// <summary>
        /// Returns the bouding box of a segment
        /// </summary>
        /// <param name="segment"></param>
        /// <returns></returns>
        public static BoundingBox GetBoundingBox(this GeoSegment segment)
        {
            BoundingBox bbox = new BoundingBox(double.MaxValue, double.MinValue, double.MaxValue, double.MinValue);

            bbox.xMin = Math.Min(segment.Start.Longitude, segment.End.Longitude);
            bbox.xMax = Math.Max(segment.Start.Longitude, segment.End.Longitude);

            bbox.yMin = Math.Min(segment.Start.Latitude, segment.End.Latitude);
            bbox.yMax = Math.Max(segment.Start.Latitude, segment.End.Latitude);

            return bbox;
        }

        /// <summary>
        /// Problem here : self intersecting lines are not supported. Not ideal for GPS tracks...
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static IGeometry ParseGeoPointAsGeometryLine(IEnumerable<GeoPoint> points)
		{
            return new LineString(points.Select(pt => new Coordinate(pt.Longitude, pt.Latitude)).ToArray()) { SRID = WGS84_SRID };
		}
        public static IGeometry ParseGeoPointAsGeometryLine(params GeoPoint[] points)
        {
            return new LineString(points.Select(pt => new Coordinate(pt.Longitude, pt.Latitude)).ToArray()) { SRID = WGS84_SRID };
        }


        //Check if the lines are interescting in 2d space
        //Alternative version from http://thirdpartyninjas.com/blog/2008/10/07/line-segment-intersection/
        /// <summary>
        /// Check if two lines intersects
        /// </summary>
        /// <param name="intersection">Ouputs the lines intersection point if they intersect, otherwise null</param>
        /// <param name="line1">Any segment of the first line</param>
        /// <param name="line2">Any segment of the second line</param>
        /// <returns>True if lines intersects</returns>
        public static bool LineLineIntersection(out GeoPoint intersection, GeoSegment line1, GeoSegment line2)
		{
			bool isIntersecting = false;
			intersection = GeoPoint.Zero;

			//3d -> 2d
			double p1_x = line1.Start.Longitude;
			double p1_y = line1.Start.Latitude;
			double p2_x = line1.End.Longitude;
			double p2_y = line1.End.Latitude;
			double p3_x = line2.Start.Longitude;
			double p3_y = line2.Start.Latitude;
			double p4_x = line2.End.Longitude;
			double p4_y = line2.End.Latitude;


			double denominator = (p4_y - p3_y) * (p2_x - p1_x) - (p4_x - p3_x) * (p2_y - p1_y);

			//Make sure the denominator is > 0, if so the lines are parallel
			if (denominator != 0)
			{
				double u_a = ((p4_x - p3_x) * (p1_y - p3_y) - (p4_y - p3_y) * (p1_x - p3_x)) / denominator;
				double u_b = ((p2_x - p1_x) * (p1_y - p3_y) - (p2_y - p1_y) * (p1_x - p3_x)) / denominator;

				//Is intersecting if u_a and u_b are between 0 and 1
				if (u_a >= 0 && u_a <= 1 && u_b >= 0 && u_b <= 1)
				{
					intersection = new GeoPoint(p1_y + u_a * (p2_y - p1_y), p1_x + u_a * (p2_x - p1_x));
					isIntersecting = true;
				}
			}

			return isIntersecting;
		}

        /// <summary>
        /// Return simple statistics from a list of points, <see cref="ElevationMetrics"/>
        /// </summary>
        /// <param name="points">Input list of points</param>
        /// <returns><see cref="ElevationMetrics"/> object</returns>
		public static ElevationMetrics ComputeMetrics(List<GeoPoint> points)
		{
			ElevationMetrics metrics = new ElevationMetrics();
			double total = 0;
			double minElevation = double.MaxValue;
			double maxElevation = double.MinValue;
			double totalClimb = 0;
			double totalDescent = 0;
			if (points.Count > 1)
			{
				double lastElevation = points[0].Elevation.GetValueOrDefault(0);
				for (int i = 1; i < points.Count; i++)
				{
					GeoPoint curPoint = points[i];
					double v_dist = DistanceTo(curPoint, points[i - 1]);
					total += v_dist;
					curPoint.DistanceFromOriginMeters = total;

					minElevation = Math.Min(minElevation, curPoint.Elevation.GetValueOrDefault(double.MaxValue));
					maxElevation = Math.Max(maxElevation, curPoint.Elevation.GetValueOrDefault(double.MinValue));

					double currentElevation = curPoint.Elevation.GetValueOrDefault(lastElevation);
					double diff = currentElevation - lastElevation;
					if (diff > 0)
					{
						totalClimb += diff;
					}
					else
					{
						totalDescent += diff;
					}
					lastElevation = currentElevation;

				}
			}
			metrics.Climb = totalClimb;
			metrics.Descent = totalDescent;
			metrics.NumPoints = points.Count;
			metrics.Distance = total;
			metrics.MinElevation = minElevation;
			metrics.MaxElevation = maxElevation;

			return metrics;
		}

        /// <summary>
        /// Returns total length of line
        /// </summary>
        /// <param name="lineWKT">LIne as geometry WKT</param>
        /// <returns></returns>
		public static double GetLength(string lineWKT)
		{
            return ParseWKTAsGeometry(lineWKT).Segments().Sum(seg => seg.Start.DistanceTo(seg.End));
		}

        /// <summary>
        /// Computes spherical distance between to locations
        /// </summary>
        /// <param name="pt1">First location</param>
        /// <param name="pt2">Second location</param>
        /// <returns>Distance in meters</returns>
		public static double DistanceTo(this GeoPoint pt1, GeoPoint pt2)
		{
			if ((pt1 == null) || (pt2 == null))
				return 0;
			else
			{
				double v_thisLatitude = pt1.Latitude;
				double v_otherLatitude = pt2.Latitude;
				double v_thisLongitude = pt1.Longitude;
				double v_otherLongitude = pt2.Longitude;

				double v_deltaLatitude = Math.Abs(pt1.Latitude - pt2.Latitude);
				double v_deltaLongitude = Math.Abs(pt1.Longitude - pt2.Longitude);

				if (v_deltaLatitude == 0 && v_deltaLongitude == 0)
					return 0;

				v_thisLatitude *= RADIAN;
				v_otherLatitude *= RADIAN;
				v_deltaLongitude *= RADIAN;

				double v_cos = Math.Cos(v_deltaLongitude) * Math.Cos(v_thisLatitude) * Math.Cos(v_otherLatitude) +
											 Math.Sin(v_thisLatitude) * Math.Sin(v_otherLatitude);

				double v_ret = EARTH_RADIUS * Math.Acos(v_cos);
				if (double.IsNaN(v_ret)) // points nearly the same
				{
					v_ret = 0d;
				}
				return v_ret;
			}
		}

        /// <summary>
        /// Computes the total length of a line
        /// </summary>
        /// <param name="points">Points where first is line start, and last is line end</param>
        /// <returns>Line length in meters</returns>
		public static double GetLineLength_Meters(List<GeoPoint> points)
		{
			double total = 0;
			try
			{
				if (points.Count > 1)
				{
					for (int v_i = 1; v_i < points.Count; v_i++)
					{
						double v_dist = DistanceTo(points[v_i], points[v_i - 1]);
						total += v_dist;
					}
				}
			}
			catch
			{
				throw;
			}
			return total;
		}


        #region Enumerators

        /// <summary>
        /// Enumerates through a geometry sub parts
        /// </summary>
        /// <param name="geom"></param>
        /// <returns></returns>
        public static IEnumerable<IGeometry> Geometries(this IGeometry geom)
        {
            for (int i = 0; i < geom.NumGeometries; i++)
            {
                yield return geom.GetGeometryN(i);
            }
        }

        /// <summary>
        /// Enumerates through a line geometry segments
        /// </summary>
        /// <param name="lineGeom"></param>
        /// <returns></returns>
        /// <remarks>Only iterates if geometry is a line string</remarks>
        public static IEnumerable<GeoSegment> Segments(this IGeometry lineGeom)
        {

            if (lineGeom == null || lineGeom.IsEmpty)
            {
                yield return null;
            }
            if (lineGeom.OgcGeometryType != OgcGeometryType.LineString)
            {
                yield return null;
            }
            if (lineGeom.NumPoints < 2)
            {
                yield return null;
            }

            for (int i = 0; i < lineGeom.NumPoints-1; i++)
            {
                Coordinate[] segCoords = new Coordinate[2];
                segCoords[0] = lineGeom.Coordinates[i];
                segCoords[1] = lineGeom.Coordinates[i + 1];

                yield return new GeoSegment(lineGeom.Coordinates[i].ToGeoPoint(), lineGeom.Coordinates[i + 1].ToGeoPoint());
            }
        }

        #endregion

        /// <summary>
        /// Transform a NTS coordinate to a DEM.Net geopoint
        /// </summary>
        /// <param name="coord"></param>
        /// <returns></returns>
        public static GeoPoint ToGeoPoint(this Coordinate coord)
        {

            if (coord == null)
            {
                return null;
            }

            return new GeoPoint(coord.Y, coord.X);
        }



    }
}
