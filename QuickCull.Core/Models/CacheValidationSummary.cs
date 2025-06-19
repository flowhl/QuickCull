using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Models
{
    public class CacheValidationSummary
    {
        public int TotalImages { get; set; }
        public int ConsistentImages { get; set; }
        public int InconsistentImages { get; set; }
        public int ValidationErrors { get; set; }
        public List<string> InconsistentFilenames { get; set; } = new();
        public List<string> ErrorFilenames { get; set; } = new();

        public double ConsistencyPercentage => TotalImages > 0 ? (double)ConsistentImages / TotalImages * 100 : 0;
        public bool HasInconsistencies => InconsistentImages > 0;
        public bool HasErrors => ValidationErrors > 0;

        public string Summary => $"Cache Validation: {ConsistentImages}/{TotalImages} consistent ({ConsistencyPercentage:F1}%), " +
                               $"{InconsistentImages} inconsistent, {ValidationErrors} errors";
    }
}
