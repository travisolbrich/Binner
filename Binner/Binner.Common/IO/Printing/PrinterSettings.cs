﻿namespace Binner.Common.IO.Printing
{
    public class PrinterSettings : IPrinterSettings
    {
        public string PrinterName { get; set; } = "Dymo LabelWriter 450";
        public string LabelName { get; set; } = "30346"; // LW 1/2" x 1 7/8"
        public LabelSource LabelSource { get; set; } = LabelSource.Default;
        public string Font { get; set; } = "Segoe UI";
    }
}
