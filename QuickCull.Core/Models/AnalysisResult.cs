using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Models
{
    public class AnalysisResult
    {
        public string Filename { get; set; }
        public string FilePath { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public string ModelVersion { get; set; }
        public string AnalysisVersion { get; set; }

        public double SharpnessOverall { get; set; }
        public double SharpnessSubject { get; set; }
        public int SubjectCount { get; set; }
        public List<string> SubjectTypes { get; set; }
        public double SubjectSharpnessPercentage { get; set; }
        public bool EyesOpen { get; set; }
        public double EyeConfidence { get; set; }
        public int PredictedRating { get; set; }
        public double PredictionConfidence { get; set; }

        //Grouping
        public int GroupID { get; set; } = -1;

        // Future extensibility
        public Dictionary<string, object> ExtendedData { get; set; } = new();
    }

}
