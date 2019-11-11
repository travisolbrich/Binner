﻿using BarcodeLib;
using System.Drawing;

namespace Binner.Common.IO.Printing
{
    /// <summary>
    /// Barcode generator
    /// </summary>
    public class BarcodeGenerator : IBarcodeGenerator
    {
        /// <summary>
        /// Generate a Code 128 Barcode
        /// </summary>
        /// <param name="partNumber">Part number to identify</param>
        /// <param name="width">Barcode image width</param>
        /// <param name="height">Barcode image height</param>
        /// <returns></returns>
        public Bitmap GenerateBarcode(string partNumber, int width, int height)
        {
            var barcode = new Barcode();
            var image = new Bitmap(barcode.Encode(TYPE.CODE128, partNumber, width, height));
            return image;
        }
    }
}
