using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Extensions
{
    public static class ExceptionExtensions
    {
        /// <summary>
        /// Extension method to retrieve all details of an exception, including inner exceptions.
        /// </summary>
        /// <param name="exception">The exception to extract details from.</param>
        /// <returns>A string containing all details about the exception.</returns>
        public static string GetFullDetails(this Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            var detailsBuilder = new StringBuilder();

            void AppendExceptionDetails(Exception ex, int level)
            {
                string indent = new string(' ', level * 2);
                detailsBuilder.AppendLine($"{indent}Exception Type: {ex.GetType().FullName}");
                detailsBuilder.AppendLine($"{indent}Message: {ex.Message}");
                detailsBuilder.AppendLine($"{indent}Stack Trace: {ex.StackTrace}");

                if (ex.Data != null && ex.Data.Count > 0)
                {
                    detailsBuilder.AppendLine($"{indent}Data:");
                    foreach (var key in ex.Data.Keys)
                    {
                        detailsBuilder.AppendLine($"{indent}  {key}: {ex.Data[key]}");
                    }
                }

                if (ex.InnerException != null)
                {
                    detailsBuilder.AppendLine($"{indent}Inner Exception:");
                    AppendExceptionDetails(ex.InnerException, level + 1);
                }
            }

            AppendExceptionDetails(exception, 0);

            return detailsBuilder.ToString();
        }
    }
}
