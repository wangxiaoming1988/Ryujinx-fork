using Ryujinx.HLE.HOS.Services.Hid;
using SDL;
using static SDL.SDL3;
using System;

namespace Ryujinx.Input.SDL3
{
    /// <summary>
    /// Manages a HID handle of a gamepad to encode and write HD rumble commands for Nin controllers.
    /// </summary>
    public unsafe class NpadHdRumble : IDisposable
    {
        private readonly SDL_hid_device* _hidHandle;

        private int _globalCount;

        private NpadHdRumble(SDL_hid_device* hidHandle)
        {
            _hidHandle = hidHandle;
        }

        public static NpadHdRumble Create(SDL_Gamepad* gamepadHandle)
        {
            ushort vendor = SDL_GetGamepadVendor(gamepadHandle);
            if (vendor != 0x057e)
            {
                return null;
            }

            ushort product = SDL_GetGamepadProduct(gamepadHandle);
            if (product != 0x2006 && product != 0x2007 && product != 0x2009 && product != 0x200e)
            {
                return null;
            }

            return new NpadHdRumble(SDL_hid_open(vendor, product, 0));
        }

        // Some of the code was translated from https://github.com/MIZUSHIKI/JoyShockLibrary-plus-HDRumble
        private void WriteHdRumble(
            int encLeftLowFreq, int encLeftLowAmp,
            int encLeftHighFreq, int encLeftHighAmp,
            int encRightLowFreq, int encRightLowAmp,
            int encRightHighFreq, int encRightHighAmp)
        {
            byte[] buf = new byte[10];

            buf[0] = 0x10;
            buf[1] = (byte)((++_globalCount) & 0xF);

            buf[2] = (byte)(encLeftHighFreq & 0xFF);
            buf[3] = (byte)(encLeftHighAmp + ((encLeftHighFreq >> 8) & 0xFF));
            buf[4] = (byte)(encLeftLowFreq + ((encLeftLowAmp >> 8) & 0xFF));
            buf[5] = (byte)(encLeftLowAmp & 0xFF);

            buf[6] = (byte)(encRightHighFreq & 0xFF);
            buf[7] = (byte)(encRightHighAmp + ((encRightHighFreq >> 8) & 0xFF));
            buf[8] = (byte)(encRightLowFreq + ((encRightLowAmp >> 8) & 0xFF));
            buf[9] = (byte)(encRightLowAmp & 0xFF);

            if (_globalCount > 0xF)
            {
                _globalCount = 0x0;
            }

            fixed (byte* ptr = buf)
            {
                SDL_hid_write(_hidHandle, ptr, (nuint)buf.Length);
            }
        }


        private static int EncodeLowFreq(float lowFreq)
        {
            float lf = Math.Clamp(lowFreq, 40.875885f, 626.286133f);
            return (int)Math.Round(32 * Math.Log2(lf * 0.1f)) - 0x40;
        }

        private static int EncodeHighFreq(float highFreq)
        {
            float hf = Math.Clamp(highFreq, 81.75177f, 1252.572266f);
            return ((int)Math.Round(32 * Math.Log2(hf * 0.1f)) - 0x60) * 4;
        }

        private static int EncodeLowAmp(float rawAmp)
        {
            int encodedAmp = 0;

            if (rawAmp is > 0 and < 0.012f)
            {
                encodedAmp = 1;
            }
            else if (rawAmp is >= 0.012f and < 0.112f)
            {
                encodedAmp = (int)Math.Round(4 * Math.Log2(rawAmp * 110f));
            }
            else if (rawAmp is >= 0.112f and < 0.225f)
            {
                encodedAmp = (int)Math.Round(16 * Math.Log2(rawAmp * 17f));
            }
            else if (rawAmp is >= 0.225f and <= 1f)
            {
                encodedAmp = (int)Math.Round(32 * Math.Log2(rawAmp * 8.7f));
            }

            return (int)Math.Floor(encodedAmp / 2.0) + 64;
        }

        private static int EncodeHighAmp(float rawAmp)
        {
            int encodedAmp = 0;

            if (rawAmp is > 0 and < 0.012f)
            {
                encodedAmp = 1;
            }
            else if (rawAmp is >= 0.012f and < 0.112f)
            {
                encodedAmp = (int)Math.Round(4 * Math.Log2(rawAmp * 110f));
            }
            else if (rawAmp is >= 0.112f and < 0.225f)
            {
                encodedAmp = (int)Math.Round(16 * Math.Log2(rawAmp * 17f));
            }
            else if (rawAmp is >= 0.225f and <= 1f)
            {
                encodedAmp = (int)Math.Round(32 * Math.Log2(rawAmp * 8.7f));
            }

            return encodedAmp * 2;
        }

        public bool HdRumble(VibrationValue left, VibrationValue right)
        {
            WriteHdRumble(EncodeLowFreq(left.FrequencyLow),
                EncodeLowAmp(left.AmplitudeLow),
                EncodeHighFreq(left.FrequencyHigh),
                EncodeHighAmp(left.AmplitudeHigh),
                EncodeLowFreq(right.FrequencyLow),
                EncodeLowAmp(right.AmplitudeLow),
                EncodeHighFreq(right.FrequencyHigh),
                EncodeHighAmp(right.AmplitudeHigh));
            return true;
        }

        public void Dispose()
        {
            SDL_hid_close(_hidHandle);
        }
    }
}
