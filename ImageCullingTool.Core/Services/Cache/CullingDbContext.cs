using ImageCullingTool.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Services.Cache
{
    public class CullingDbContext : DbContext
    {
        private readonly string _dbPath;

        public CullingDbContext(string folderPath)
        {
            _dbPath = Path.Combine(folderPath, ".culling_cache.db");

            // Ensure database directory exists
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public DbSet<ImageAnalysis> Images { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");

            // Optional: Enable detailed logging in debug mode
#if DEBUG
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information);
#endif
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ImageAnalysis>(entity =>
            {
                // Primary key
                entity.HasKey(e => e.Filename);
                entity.Property(e => e.Filename)
                    .HasMaxLength(260) // Windows path limit
                    .IsRequired();

                // String properties with reasonable limits (nullable)
                entity.Property(e => e.ImageFormat).HasMaxLength(10).IsRequired(false);
                entity.Property(e => e.FileHash).HasMaxLength(32).IsRequired(false);
                entity.Property(e => e.AnalysisVersion).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ModelVersion).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.SubjectTypes).HasMaxLength(500).IsRequired(false);
                entity.Property(e => e.LightroomLabel).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ColorAnalysis).HasMaxLength(1000).IsRequired(false);
                entity.Property(e => e.ExtendedAnalysisJson).HasColumnType("TEXT").IsRequired(false);

                // Indexes for common query patterns
                entity.HasIndex(e => e.AnalysisDate)
                    .HasDatabaseName("IX_Images_AnalysisDate");

                entity.HasIndex(e => e.PredictedRating)
                    .HasDatabaseName("IX_Images_PredictedRating");

                entity.HasIndex(e => e.SharpnessOverall)
                    .HasDatabaseName("IX_Images_SharpnessOverall");

                entity.HasIndex(e => e.LightroomRating)
                    .HasDatabaseName("IX_Images_LightroomRating");

                entity.HasIndex(e => e.IsRaw)
                    .HasDatabaseName("IX_Images_IsRaw");

                entity.HasIndex(e => e.HasXmp)
                    .HasDatabaseName("IX_Images_HasXmp");

                // Composite index for common filter combinations
                entity.HasIndex(e => new { e.PredictedRating, e.SharpnessOverall })
                    .HasDatabaseName("IX_Images_Rating_Sharpness");
            });
        }

        /// <summary>
        /// Ensures the database exists and applies any pending migrations
        /// </summary>
        public async Task EnsureDatabaseCreatedAsync()
        {
            try
            {
                await Database.EnsureCreatedAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create database at {_dbPath}", ex);
            }
        }

        /// <summary>
        /// Gets the database file path
        /// </summary>
        public string DatabasePath => _dbPath;
    }
}
