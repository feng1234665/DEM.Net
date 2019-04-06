﻿using AssetGenerator;
using AssetGenerator.Runtime;
using DEM.Net.glTF;
using DEM.Net.glTF.Export;
using DEM.Net.Lib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SampleApp
{
    class STLSamples
    {
        public static void Run(string outputDirectory, string modelName, string bboxWKT, DEMDataSet dataset)
        {
           
            // small test
            //string bboxWKT = "POLYGON ((5.558267 43.538602, 5.557902 43.538602, 5.557902 43.538353, 5.558267 43.538353, 5.558267 43.538602))";// zoom ste
            RasterService rasterService = new RasterService();
            ElevationService elevationService = new ElevationService(rasterService);

            var bbox = GeometryService.GetBoundingBox(bboxWKT);
            //bbox = bbox.Scale(1.3); // test
            var heightMap = elevationService.GetHeightMap(bbox, dataset);
            heightMap = heightMap.ReprojectGeodeticToCartesian()
                                    .ZScale(2f)
                                    .CenterOnOrigin()
                                    .FitInto(250f)
                                    .BakeCoordinates();
            glTFService glTFService = new glTFService();
            var mesh = glTFService.GenerateTriangleMesh_Boxed(heightMap);

            // STL axis differ from glTF 
            mesh.RotateX((float)Math.PI / 2f);

            var stlFileName = Path.Combine(outputDirectory, $"{modelName}.stl");
            STLExportService stlService = new STLExportService();
            stlService.STLExport(mesh, stlFileName, false);

            Model model = glTFService.GenerateModel(mesh, modelName);
            glTFService.Export(model, outputDirectory, $"{modelName}", false, true);

        }

        
    }
}
