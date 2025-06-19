using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuickCull.Core.Helper
{
    public static class XMPHelper
    {
        public static string CleanXmpString(string xmpString)
        {
            // Remove all BOM characters
            xmpString = Regex.Replace(xmpString, @"[\uFEFF\uFFFE\uFFFF]", "");

            // Fix the xpacket begin attribute specifically
            xmpString = Regex.Replace(xmpString, @"begin=""[^\""]*""", "begin=\"\"");

            return xmpString.Trim();
        }
    }
}
