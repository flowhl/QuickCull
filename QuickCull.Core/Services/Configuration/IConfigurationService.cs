﻿using QuickCull.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Configuration
{
    public interface IConfigurationService
    {
        Task<AnalysisConfiguration> LoadConfigurationAsync();
        Task SaveConfigurationAsync(AnalysisConfiguration config);
        Task<string> GetDefaultModelPathAsync();
    }
}
