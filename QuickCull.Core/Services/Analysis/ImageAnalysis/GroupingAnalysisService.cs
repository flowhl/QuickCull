using QuickCull.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using QuickCull.Core.Services.Thumbnail;

namespace QuickCull.Core.Services.Analysis.ImageAnalysis
{
    public class GroupingAnalysisService : IImageBatchAnalysisService
    {
        private readonly IThumbnailService _thumbnailService;
        private readonly double _SimilarityThreshold = 0.8;
        
        private readonly double _histogramSimilarityWeight = 0.7; // Weight for histogram similarity in combined score
        private readonly double _featureCountWeight = 0.3; // Weight for feature count similarity in combined score
        private readonly int _maxGroupSize = 50;

        public GroupingAnalysisService(IThumbnailService thumbnailService)
        {
            _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        }

        public async Task<List<AnalysisResult>> AnalyzeImageBatchAsync(List<AnalysisResult> results)
        {
            if (results == null || results.Count == 0)
                return results;

            try
            {
                // Load images and compute features
                var imageData = await LoadImageDataAsync(results);

                // Group images based on visual similarity
                var groups = GroupImagesBySimilarity(imageData);

                // Assign group IDs to results
                AssignGroupIds(results, groups);

                return results;
            }
            catch (Exception ex)
            {
                // Log error and return original results
                Trace.WriteLine($"Error in grouping analysis: {ex.Message}");
                return results;
            }
        }

        private async Task<List<ImageAnalysisData>> LoadImageDataAsync(List<AnalysisResult> results)
        {
            var imageDataList = new List<ImageAnalysisData>();

            await Task.Run(() =>
            {
                Parallel.ForEach(results, result =>
                {
                    string thumbnailPath = _thumbnailService.GetThumbnailPath(result.FilePath);
                    try
                    {
                        if (File.Exists(thumbnailPath))
                        {
                            //TODO CV2 wont read raw images
                            using var image = Cv2.ImRead(thumbnailPath);
                            if (!image.Empty())
                            {
                                var data = new ImageAnalysisData
                                {
                                    Filename = result.FilePath,
                                    Histogram = ComputeColorHistogram(image),
                                    Features = ExtractORBFeatures(image)
                                };

                                lock (imageDataList)
                                {
                                    imageDataList.Add(data);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error processing image {result.Filename}: {ex.Message}");
                    }
                });
            });

            return imageDataList;
        }

        private Mat ComputeColorHistogram(Mat image)
        {
            // Convert to HSV for better color representation
            using var hsvImage = new Mat();
            Cv2.CvtColor(image, hsvImage, ColorConversionCodes.BGR2HSV);

            // Calculate histogram for H and S channels
            var hist = new Mat();
            var channels = new int[] { 0, 1 }; // H and S channels
            var histSize = new int[] { 50, 60 }; // Bins for H and S
            var ranges = new Rangef[]
            {
                new Rangef(0, 180), // H range
                new Rangef(0, 256)  // S range
            };

            Cv2.CalcHist(new Mat[] { hsvImage }, channels, null, hist, 2, histSize, ranges);
            Cv2.Normalize(hist, hist, 0, 1, NormTypes.MinMax);

            return hist;
        }

        private KeyPoint[] ExtractORBFeatures(Mat image)
        {
            try
            {
                // Convert to grayscale
                using var grayImage = new Mat();
                Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);

                // Create ORB detector
                using var orb = ORB.Create(500); // Limit to 500 features

                // Detect keypoints and compute descriptors
                using var descriptors = new Mat();
                orb.DetectAndCompute(grayImage, null, out var keypoints, descriptors);

                // We only need keypoints for basic similarity
                return keypoints ?? new KeyPoint[0];
            }
            catch
            {
                return new KeyPoint[0];
            }
        }

        private List<List<string>> GroupImagesBySimilarity(List<ImageAnalysisData> imageData)
        {
            var groups = new List<List<string>>();
            var processed = new HashSet<string>();

            foreach (var currentImage in imageData)
            {
                if (processed.Contains(currentImage.Filename))
                    continue;

                var currentGroup = new List<string> { currentImage.Filename };
                processed.Add(currentImage.Filename);

                // Find similar images
                foreach (var otherImage in imageData)
                {
                    if (processed.Contains(otherImage.Filename) ||
                        currentGroup.Count >= _maxGroupSize)
                        continue;

                    if (AreImagesSimilar(currentImage, otherImage))
                    {
                        currentGroup.Add(otherImage.Filename);
                        processed.Add(otherImage.Filename);
                    }
                }

                groups.Add(currentGroup);
            }

            return groups;
        }

        private bool AreImagesSimilar(ImageAnalysisData image1, ImageAnalysisData image2)
        {
            // Compare histograms
            var histogramSimilarity = Cv2.CompareHist(image1.Histogram, image2.Histogram, HistCompMethods.Correl);

            // Simple feature count similarity (more sophisticated matching could be implemented)
            var featureCountDiff = Math.Abs(image1.Features.Length - image2.Features.Length);
            var maxFeatureCount = Math.Max(image1.Features.Length, image2.Features.Length);
            var featureSimilarity = maxFeatureCount > 0 ? 1.0 - (double)featureCountDiff / maxFeatureCount : 1.0;

            // Combine similarities (weighted average)
            var combinedSimilarity = (histogramSimilarity * _histogramSimilarityWeight) + (featureSimilarity * _featureCountWeight);

            return combinedSimilarity >= _SimilarityThreshold;
        }

        private void AssignGroupIds(List<AnalysisResult> results, List<List<string>> groups)
        {
            // Start group IDs from 1 (reserve 0 for ungrouped)
            for (int groupId = 0; groupId < groups.Count; groupId++)
            {
                var filenames = groups[groupId];
                foreach (var filename in filenames)
                {
                    var result = results.FirstOrDefault(r => r.FilePath == filename);
                    if (result != null)
                    {
                        result.GroupID = groupId + 1; // Groups start from 1
                    }
                }
            }

            // Group 0 remains as default for any unprocessed/ungrouped images
            // (AnalysisResult.Group already defaults to 0, so no action needed)
        }

        public void Init()
        {
            // Initialize any required resources
            // OpenCV initialization is handled automatically
        }

        private class ImageAnalysisData
        {
            public string Filename { get; set; }
            public Mat Histogram { get; set; }
            public KeyPoint[] Features { get; set; }
        }
    }
}