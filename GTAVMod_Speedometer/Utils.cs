using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using GTA;
using GTA.Native;

namespace GTAVMod_Speedometer
{
    class Utils
    {
        public static float MsToKmh(float mPerS)
        {
            return mPerS * 3600 / 1000;
        }

        public static float KmToMiles(float km)
        {
            return km * 0.6213711916666667f;
        }

        public static bool IsPlayerRidingDeer(Ped playerPed)
        {
            try
            {
                Ped attached = Function.Call<Ped>(Hash.GET_ENTITY_ATTACHED_TO, playerPed);
                if (attached != null)
                {
                    PedHash attachedHash = (PedHash)attached.Model.Hash;
                    return (attachedHash == PedHash.Deer);
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public static Color IncrementARGB(Color color, int dA, int dR, int dG, int dB)
        {
            return Color.FromArgb(Math.Max(Math.Min(color.A + dA, 255), 0), Math.Max(Math.Min(color.R + dR, 255), 0),
                Math.Max(Math.Min(color.G + dG, 255), 0), Math.Max(Math.Min(color.B + dB, 255), 0));
        }

        public static Color HSLA2RGBA(double h, double sl, double l, double a)
        {
            double v;
            double r, g, b;
            r = l;
            g = l;
            b = l;
            v = (l <= 0.5) ? (l * (1.0 + sl)) : (l + sl - l * sl);
            if (v > 0)
            {
                double m;
                double sv;
                int sextant;
                double fract, vsf, mid1, mid2;
                m = l + l - v;
                sv = (v - m) / v;
                h *= 6.0;
                sextant = (int)h;
                fract = h - sextant;
                vsf = v * sv * fract;
                mid1 = m + vsf;
                mid2 = v - vsf;
                switch (sextant)
                {
                    case 0:
                        r = v;
                        g = mid1;
                        b = m;
                        break;
                    case 1:
                        r = mid2;
                        g = v;
                        b = m;
                        break;
                    case 2:
                        r = m;
                        g = v;
                        b = mid1;
                        break;
                    case 3:
                        r = m;
                        g = mid2;
                        b = v;
                        break;
                    case 4:
                        r = mid1;
                        g = m;
                        b = v;
                        break;
                    case 5:
                        r = v;
                        g = m;
                        b = mid2;
                        break;
                }
            }
            int colorR = Math.Min(Convert.ToInt32(r * 255.0f), 255);
            int colorG = Math.Min(Convert.ToInt32(g * 255.0f), 255);
            int colorB = Math.Min(Convert.ToInt32(b * 255.0f), 255);
            int colorA = Math.Min(Convert.ToInt32(a * 255.0f), 255);
            return Color.FromArgb(colorA, colorR, colorG, colorB);
        }
    }
}
