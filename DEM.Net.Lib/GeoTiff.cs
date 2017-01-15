﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using BitMiracle.LibTiff.Classic;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using DEM.Net.Lib.Services;

namespace DEM.Net.Lib
{
	public class GeoTiff : IDisposable
	{
		Tiff _tiff;
		string _tiffPath;

		public GeoTiff(string tiffPath)
		{
			_tiffPath = tiffPath;
			_tiff = Tiff.Open(tiffPath, "r");
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				_tiff?.Dispose();
			}
		}

		~GeoTiff()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public HeightMap ConvertToHeightMap()
		{
            FileMetadata metadata = GeoTiffRepositoryService.ParseMetadata(_tiff, _tiffPath);
            HeightMap heightMap = GeoTiffRepositoryService.ParseGeoData(_tiff, metadata);
			return heightMap;
		}

        public float ParseGeoDataAtPoint(FileMetadata metadata, double lat, double lon)
        {
           return GeoTiffRepositoryService.ParseGeoDataAtPoint(_tiff, metadata, lat, lon);
        }

        public HeightMap ConvertToHeightMap(BoundingBox bbox, FileMetadata metadata)
        {
            HeightMap heightMap = GeoTiffRepositoryService.ParseGeoDataInBBox(_tiff, bbox, metadata);
            return heightMap;
        }
	}
}