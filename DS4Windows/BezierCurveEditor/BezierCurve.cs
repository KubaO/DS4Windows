﻿/* MIT License
 *
 * KeySpline - use bezier curve for transition easing function
 * Copyright (c) 2012 Gaetan Renaudeau <renaudeau.gaetan@gmail.com> (GRE)
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
/* KeySpline - use bezier curve for transition easing function is inspired from Firefox's nsSMILKeySpline.cpp */

/*
* This file contains the original bezier curve code (see comments above) and calculations ported as C# code. The original code was in JavaScript.
*
* This file has few customizations and optimizations for the needs of DS4Windows application (see https://github.com/Ryochan7/DS4Windows).
* MIT License. Permission is hereby granted, free of charge, to any person to do whatever they want with this C# ported version of BezierCurve calculation code 
* as long this part of the code is open sourced and usage is in compliance with the above shown original license, also.
* 
* Copyright (c) 2019, MIKA-N (https://github.com/mika-n). 
* 
* The original JavaScript version of bezier easing made by GRE (https://github.com/gre/bezier-easing).
* 
* Usage:
*    BezierCurve.InitBezierCurve = Initialize bezier curve and output lookup table. Must be called at least once before calling GetBezierEasing method (or accessing lookup table directly) to re-map analog axis input.
*    BezierCurve.GetBezierEasing = Return re-mapped output value for an input axis value (or alternatively directly accessing the lookup table BezierCurve.arrayBezierLUT[inputVal] if even tiny CPU cycles matter)
* 
*/
using System;

namespace DS4Windows
{
    public class BezierCurve
    {
        public enum AxisType { LSRS, L2R2, SA };

        private static int kSplineTableSize = 11;
        private static double kSampleStepSize = 1.0 / (kSplineTableSize - 1.0);
        private double[] arraySampleValues;

        // These values are established by empiricism with tests (tradeoff: performance VS precision) (comment by GRE)
        private static int    NEWTON_ITERATIONS = 4;
        private static double NEWTON_MIN_SLOPE = 0.001;
        private static double SUBDIVISION_PRECISION = 0.0000001;
        private static int    SUBDIVISION_MAX_ITERATIONS = 10;

        private double mX1 = 0, mY1 = 0, mX2 = 0, mY2 = 0;  // Bezier curve definition (0, 0, 0, 0 = Linear. 99, 99, 0, 0 = Pre-defined hard-coded EnhancedPrecision curve)

        // Set or Get string representation of the bezier curve definition value (Note! Set doesn't initialize the lookup table. InitBezierCurve needs to be called to actually initalize the calculation)
        public string AsString
        {
            get { return ($"{mX1}, {mY1}, {mX2}, {mY2}"); }
            set
            {
                // Set bezier curve defintion from a string value (4 comma separated decimals). If any of the string values are invalid then set curve as linear "zero" curve
                string[] bezierDef = value.Split(new Char[] { ',' }, 4);
                if (bezierDef.Length < 4 || !Double.TryParse(bezierDef[0], out mX1) || !Double.TryParse(bezierDef[1], out mY1) || !Double.TryParse(bezierDef[2], out mX2) || !Double.TryParse(bezierDef[3], out mY2) )
                    mX1 = mY1 = mX2 = mY2 = 0;
            }
        }

        // Custom definition set by DS4Windows options screens. This string is not validated (ie. the value is as user entered it and could be an invalid curve definition). This value is saved in a profile XML file.
        public string CustomDefinition { get; set; }
        public string ToString() { return this.CustomDefinition; }

        public AxisType axisType;

        // Lookup result table is always either in 0..128 or 0..255 range depending on the DS4 analog axis range. LUT table set as public to let DS4Win reading thread to access it directly (every CPU cycle matters)
        public byte[] arrayBezierLUT = null;  

        public BezierCurve()
        {
            CustomDefinition = "";
        }

        public bool InitBezierCurve(string bezierCurveDefinition, AxisType gamepadAxisType, bool setCustomDefinitionProperty = false)
        {
            if (setCustomDefinitionProperty)
                this.CustomDefinition = bezierCurveDefinition;

            this.AsString = bezierCurveDefinition;
            return InitBezierCurve(mX1, mY1, mX2, mY2, gamepadAxisType);
        }

        public bool InitBezierCurve(double x1, double y1, double x2, double y2, AxisType gamepadAxisType)
        {
            bool bRetValue = true;

            if (arrayBezierLUT == null)
                arrayBezierLUT = new byte[256];

            if (x1 == 99.0)
            {
                // If curve is "99.0, 99.0, 0.0, 0.0" then curve is a pre-defined fixed EnchPrecision curve and not a customizable bezier curve
                if (y1 == 99.0) return InitEnhancedPrecision(gamepadAxisType);
            }

            if (x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1)
            {
                // throw new Exception("INVALID VALUE. BezierCurve X1 and X2 should be in [0, 1] range");
                AppLogger.LogToGui($"WARNING. Invalid custom bezier curve \"{x1}, {y1}, {x2}, {y2}\" in {gamepadAxisType} axis. x1 and x2 should be in 0..1 range. Using linear curve.", true);
                mX1 = mY1 = mX2 = mY2 = 0;
                bRetValue = false;
            }
            else
            {
                mX1 = x1;
                mY1 = y1;
                mX2 = x2;
                mY2 = y2;
            }
            axisType = gamepadAxisType;

            // If this is linear definition then init the lookup table with 1-on-1 mapping
            if (x1 == 0 && y1 == 0 && ((x2 == 0 && y2 == 0) || (x2 == 1 && y2 == 1)))
            {
                for (int idx = 0; idx <= 255; idx++)
                    arrayBezierLUT[idx] = (byte)idx;

                return bRetValue;
            }

            try
            {
                double axisMaxDouble;
                double axisCenterPosDouble;

                switch (gamepadAxisType)
                {
                    case AxisType.LSRS:
                        axisMaxDouble = 127;     // DS4 LS/RS axis has a "center position" at 128. Left turn has 0..127 positions and right turn 128..255 positions. 
                        axisCenterPosDouble = 128;
                        break;

                    case AxisType.L2R2:
                        axisMaxDouble = 255;    // L2R2 analog trigger range 0..255
                        axisCenterPosDouble = 0;
                        break;

                    default:
                        axisMaxDouble = 128;    // SixAxis x/z/y range 0..128
                        axisCenterPosDouble = 0;
                        break;
                }

                arraySampleValues = new double[BezierCurve.kSplineTableSize];
                for (int idx = 0; idx < BezierCurve.kSplineTableSize; idx++)
                    arraySampleValues[idx] = CalcBezier(idx * BezierCurve.kSampleStepSize, mX1, mX2);

                // Pre-populate lookup result table for GetBezierEasing function (performance optimization)
                for (byte idx = 0; idx <= (byte)axisMaxDouble; idx++)
                {
                    arrayBezierLUT[idx + (byte)axisCenterPosDouble] = (byte)(Global.Clamp(0, Math.Round(CalcBezier(getTForX(idx / axisMaxDouble), mY1, mY2) * axisMaxDouble), axisMaxDouble) + axisCenterPosDouble);

                    // Invert curve from a right side of the center position (128) to the left tilted stick axis (or from up tilt to down tilt)
                    if (gamepadAxisType == AxisType.LSRS)
                        arrayBezierLUT[127 - idx] = (byte)(255 - arrayBezierLUT[idx + (byte)axisCenterPosDouble]);

                    // If the axisMaxDouble is 255 then we need this to break the look (byte is unsigned 0..255, so the FOR loop never reaches 256 idx value. C# would throw an overflow exceptio)
                    if (idx == axisMaxDouble) break;
                }
            }
            finally
            {
                arraySampleValues = null;
            }

            return bRetValue;
        }

        // Initialize a special "hard-coded" and pre-defined EnhancedPrecision output curve as a lookup result table
        private bool InitEnhancedPrecision(AxisType gamepadAxisType)
        {
            mX1 = mY1 = 99; // x1=99, y1=99 is a dummy curve identifier of hard-coded EnhancedPrecision "curve"
            mX2 = mY2 = 0;
            axisType = gamepadAxisType;

            double axisMaxDouble;
            double axisCenterPosDouble;

            switch (gamepadAxisType)
            {
                case AxisType.LSRS:
                    axisMaxDouble = 127;     // DS4 LS/RS axis has a "center position" at 128. Left turn has 0..127 positions and right turn 128..255 positions. 
                    axisCenterPosDouble = 128;
                    break;

                case AxisType.L2R2:
                    axisMaxDouble = 255;    // L2R2 analog trigger range 0..255
                    axisCenterPosDouble = 0;
                    break;

                default:
                    axisMaxDouble = 128;    // SixAxis x/z/y range 0..128
                    axisCenterPosDouble = 0;
                    break;
            }

            double abs, output;
            // double temp, cap, sign;

            for (byte idx = 0; idx <= axisMaxDouble; idx++)
            {
                //cap = dState.RX >=128 ? 127.0 : 128.0;
                //temp = (dState.RX - 128.0) / cap;
                //sign = temp >= 0.0 ? 1.0 : -1.0;
                //abs = Math.Abs(temp);
                //output = 0.0;

                abs = idx / axisMaxDouble; 
                if (abs <= 0.4)
                    output = 0.55 * abs;
                else if (abs <= 0.75)
                    output = abs - 0.18;
                else //if (abs > 0.75)
                    output = (abs * 1.72) - 0.72;

                // dState.RX = output * sign * cap + 128.0;
                arrayBezierLUT[idx + (byte)axisCenterPosDouble] = (byte)(output * axisMaxDouble + axisCenterPosDouble);

                // Invert curve from a right side of the center position (128) to the left tilted stick axis (or from up tilt to down tilt)
                if (gamepadAxisType == AxisType.LSRS)
                    arrayBezierLUT[127 - idx] = (byte)(255 - arrayBezierLUT[idx + (byte)axisCenterPosDouble]);

                // If the axisMaxDouble is 255 then we need this to break the look (byte is unsigned 0..255, so the FOR loop never reaches 256 idx value. C# would throw an overflow exceptio)
                if (idx == axisMaxDouble) break;
            }
            return true;
        }

        public byte GetBezierEasing(byte inputXValue) 
        {
            unchecked
            {
                return (arrayBezierLUT == null ? inputXValue : arrayBezierLUT[inputXValue]);
                //return (byte)(Global.Clamp(0, Math.Round(CalcBezier(getTForX(inputXValue / 255), mY1, mY2) * 255), 255));
            }
        }

        private double A(double aA1, double aA2) { return 1.0 - 3.0 * aA2 + 3.0 * aA1; }
        private double B(double aA1, double aA2) { return 3.0 * aA2 - 6.0 * aA1; }
        private double C(double aA1) { return 3.0 * aA1; }

        private double CalcBezier(double aT, double aA1, double aA2)
        {
            return ((A(aA1, aA2) * aT + B(aA1, aA2)) * aT + C(aA1)) * aT;
        }

        private double getTForX(double aX)
        {
            double intervalStart = 0.0;
            int currentSample = 1;
            int lastSample = kSplineTableSize - 1;

            for (; currentSample != lastSample && arraySampleValues[currentSample] <= aX; ++currentSample)
            {
                intervalStart += kSampleStepSize;
            }
            --currentSample;

            // Interpolate to provide an initial guess for t
            double dist = (aX - arraySampleValues[currentSample]) / (arraySampleValues[currentSample + 1] - arraySampleValues[currentSample]);
            double guessForT = intervalStart + dist * kSampleStepSize;

            double initialSlope = getSlope(guessForT, mX1, mX2);
            if (initialSlope >= NEWTON_MIN_SLOPE)
            {
                return newtonRaphsonIterate(aX, guessForT /*, mX1, mX2*/);
            }
            else if (initialSlope == 0.0)
            {
                return guessForT;
            }
            else
            {
                return binarySubdivide(aX, intervalStart, intervalStart + kSampleStepSize /*, mX1, mX2*/);
            }
        }

        // Returns dx/dt given t, x1, and x2, or dy/dt given t, y1, and y2.
        private double getSlope(double aT, double aA1, double aA2)
        {
            return 3.0 * A(aA1, aA2) * aT * aT + 2.0 * B(aA1, aA2) * aT + C(aA1);
        }

        private double newtonRaphsonIterate(double aX, double aGuessT /*, double mX1, double mX2*/)
        {
            for (int i = 0; i < BezierCurve.NEWTON_ITERATIONS; ++i)
            {
                double currentSlope = getSlope(aGuessT, mX1, mX2);
                if (currentSlope == 0.0)
                {
                    return aGuessT;
                }
                double currentX = CalcBezier(aGuessT, mX1, mX2) - aX;
                aGuessT -= currentX / currentSlope;
            }
            return aGuessT;
        }

        private double binarySubdivide(double aX, double aA, double aB /*, double mX1, double mX2*/)
        {
            double currentX, currentT, i = 0;
            do
            {
                currentT = aA + (aB - aA) / 2.0;
                currentX = CalcBezier(currentT, mX1, mX2) - aX;
                if (currentX > 0.0)
                {
                    aB = currentT;
                }
                else
                {
                    aA = currentT;
                }
            } while (Math.Abs(currentX) > BezierCurve.SUBDIVISION_PRECISION && ++i < BezierCurve.SUBDIVISION_MAX_ITERATIONS);

            return currentT;
        }

    }
}
