using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HardwareControl
{
    internal class SystemConstParams
    {
        /// <summary>
        /// `FoupNumber`.
        /// </summary>
        public const int FoupNumber = 4;

        /// <summary>
        /// `ResultDir`.
        /// </summary>
        public const string LogDir = @"D:\logs";

        /// <summary>
        /// `ResultDir`.
        /// </summary>
        public const string ResultDir = "GY\\Result"; // @"D:\GY\Result";

        /// <summary>
        /// `Salt`.
        /// </summary>
        public const string Salt = "Zv+wZ/XGnXmF0OmWNoQTICn/W2ZVuWVztfLoR5C8G5c=";

        /// <summary>
        /// `ResultDir`.
        /// </summary>
        public const string ImageFileTempPath = @"D:\GY\Result\Temp";

        /// <summary>
        /// `OkCassetteIndex`.
        /// </summary>
        public const int OkCassetteIndex = 2;

        /// <summary>
        /// `NgCassetteIndex`.
        /// </summary>
        public const int NgCassetteIndex = 1;

        /// <summary>
        /// `CassetteNumber`.
        /// </summary>
        public const int CassetteNumber = 2;

        /// <summary>
        /// AlignerTask.
        /// </summary>
        public enum AlignerTask
        {
            /// <summary>
            /// AlignerStop.
            /// </summary>
            AlignerStop = 17,
        }
    }
}
