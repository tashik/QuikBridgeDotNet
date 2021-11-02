using System;
using System.Collections.Generic;

namespace QuikBridge
{
    class BasicOptions
    {
        //кодирует имя опциона из параметров
        public static string GetOptionCode(string symbol, string optionType, decimal strike, int month, int year, DateTime expDate)
        {
            string strMonth;
            switch (month)
            {
                case 1: if (optionType == "p") { strMonth = "M"; } else { strMonth = "A"; } break;
                case 2: if (optionType == "p") { strMonth = "N"; } else { strMonth = "B"; } break;
                case 3: if (optionType == "p") { strMonth = "O"; } else { strMonth = "C"; } break;
                case 4: if (optionType == "p") { strMonth = "P"; } else { strMonth = "D"; } break;
                case 5: if (optionType == "p") { strMonth = "Q"; } else { strMonth = "E"; } break;
                case 6: if (optionType == "p") { strMonth = "R"; } else { strMonth = "F"; } break;
                case 7: if (optionType == "p") { strMonth = "S"; } else { strMonth = "G"; } break;
                case 8: if (optionType == "p") { strMonth = "T"; } else { strMonth = "H"; } break;
                case 9: if (optionType == "p") { strMonth = "U"; } else { strMonth = "I"; } break;
                case 10: if (optionType == "p") { strMonth = "V"; } else { strMonth = "J"; } break;
                case 11: if (optionType == "p") { strMonth = "W"; } else { strMonth = "K"; } break;
                case 12: if (optionType == "p") { strMonth = "X"; } else { strMonth = "L"; } break;
                default: strMonth = "0"; break;
            }

            string week = null;

            
            Int16 NumExpWeek = BasicOptions.NumberWeekOfMonth(expDate);
            int TotalWeeksInMonth = BasicOptions.TotalWeeksInMonth(expDate);

            switch (NumExpWeek)
            {
                case 1: week = "A"; break;
                case 2: week = "B"; break;
                case 3: 
                    if (symbol.StartsWith("BR") && (int)expDate.DayOfWeek == 4)
                    {
                        week = "C";
                    } else
                    {
                        week = null;
                    }
                    break;
                case 4:
                    if ((TotalWeeksInMonth == 4 || IsLastWeekInMonth(expDate)) && (int) expDate.DayOfWeek != 4 && symbol.StartsWith("BR"))
                    {
                        break;
                    }
                    week = "D";
                    break;
                case 5:
                    if ( (int)expDate.DayOfWeek != 4 && symbol.StartsWith("BR"))
                    {
                        break;
                    }
                    week = "E";
                    break;
                default: week = null; break;
            }
            
            
            string optionCode = symbol + strike.ToString() + "B" + strMonth + year.ToString();
            return (week != null) ? optionCode + week : optionCode;
        }

        public static int TotalWeeksInMonth(DateTime currentDate)
        {
            int daysInMonth = DateTime.DaysInMonth(currentDate.Year, currentDate.Month);
            DateTime firstOfMonth = new DateTime(currentDate.Year, currentDate.Month, 1);
            //days of week starts by default as Sunday = 0
            int firstDayOfMonth = (int)firstOfMonth.DayOfWeek;
            int weeksInMonth = (int)Math.Floor((firstDayOfMonth + daysInMonth) / 7.0);
            return weeksInMonth;
        }

        public static bool IsLastWeekInMonth(DateTime currentDate)
        {
            DateTime dateInAWeek = currentDate.AddDays(7);
            int monthInAWeek = dateInAWeek.Month;
            return monthInAWeek != currentDate.Month;
        }

        public static Int16 NumberWeekOfMonth(DateTime currentDate)
        {
            int numberWeekOfTheMonth = 0;
            try
            {
                var firstDayOfTheMonth = new DateTime(currentDate.Year, currentDate.Month, 1);
                var numberDayOfWeek = Convert.ToInt16(firstDayOfTheMonth.DayOfWeek.ToString("D"));
                numberWeekOfTheMonth = (currentDate.Day + numberDayOfWeek - 2) / 7 + 1;

                if (numberDayOfWeek == 5 || numberDayOfWeek == 6 || numberDayOfWeek == 7)
                {
                    numberWeekOfTheMonth -= 1;
                }
            }
            catch (Exception ex)
            {
                // add to log
            }
            return Convert.ToInt16(numberWeekOfTheMonth);
        }

        public static double[] CalcPriceAndGreeks(string optionType, double price, double strike, double t, double r, double v)
        {

            var rValue = new double[5] { 0.0, 0.0, 0.0, 0.0, 0.0 }; // price delta gamma vega theta

            v /= 100;

            var d1 = 0.0;
            var d2 = 0.0;

            if (t > 0.0)
            {
                var tsqrt = Math.Sqrt(t);

                d1 = (Math.Log(price / strike) + r * t) / (v * tsqrt) + 0.5 * v * tsqrt;
                d2 = d1 - 1.0 * v * tsqrt;

                if (optionType == "c")
                {
                    rValue[0] = price * alglib.normaldistribution(d1) - strike * Math.Exp(-r * t) * Cnd(d2);
                    rValue[1] = alglib.normaldistribution(d1);
                    rValue[2] = nd(d1) / (price * v * tsqrt);
                    rValue[3] = price * tsqrt * nd(d1) / 100;
                    rValue[4] = (-(price * v * nd(d1)) / (2 * tsqrt) - r * strike * Math.Exp(-r * t) * alglib.normaldistribution(d2)) / 365;
                }
                else 
                {
                    rValue[0] = strike * Math.Exp(-r * t) * alglib.normaldistribution(-d2) - price * alglib.normaldistribution(-d1);
                    rValue[1] = -alglib.normaldistribution(-d1);
                    rValue[2] = nd(d1) / (price * v * tsqrt);
                    rValue[3] = price * tsqrt * nd(d1) / 100;
                    rValue[4] = (-(price * v * nd(d1)) / (2 * tsqrt) + r * strike * Math.Exp(-r * t) * alglib.normaldistribution(d2)) / 365;
                }
            }
            else
            {
                if (optionType == "c")
                {
                    rValue[0] = Math.Max(price - strike, 0);
                    rValue[1] = price - strike > 0 ? 1 : 0;
                    rValue[2] = 0;
                    rValue[3] = 0;
                    rValue[4] = 0;
                }
                else 
                {
                    rValue[0] = Math.Max(strike - price, 0);
                    rValue[1] = strike - price > 0 ? -1 : 0;
                    rValue[2] = 0;
                    rValue[3] = 0;
                    rValue[4] = 0;
                }
            }

            return rValue;
        } // CalcPriceAndGreeks

        public static double[] CalcPriceAndGreeks2(string optionType, double price, double strike, double t, double r, double v)
        {

            var rValue = new double[5] { 0.0, 0.0, 0.0, 0.0, 0.0 }; // price delta gamma vega theta

            v /= 100;

            var d1 = 0.0;
            var d2 = 0.0;

            if (t > 0.0)
            {
                var tsqrt = Math.Sqrt(t);

                d1 = (Math.Log(price / strike) + r * t) / (v * tsqrt) + 0.5 * v * tsqrt;
                d2 = d1 - 1.0 * v * tsqrt;

                if (optionType == "c")
                {
                    rValue[0] = price * Cnd(d1) - strike * Math.Exp(-r * t) * Cnd(d2);
                    rValue[1] = Cnd(d1);
                    rValue[2] = nd(d1) / (price * v * tsqrt);
                    rValue[3] = price * tsqrt * nd(d1) / 100;
                    rValue[4] = (-(price * v * nd(d1)) / (2 * tsqrt) - r * strike * Math.Exp(-r * t) * Cnd(d2)) / 365;
                }
                else 
                {
                    rValue[0] = strike * Math.Exp(-r * t) * Cnd(-d2) - price * Cnd(-d1);
                    rValue[1] = -Cnd(-d1);
                    rValue[2] = nd(d1) / (price * v * tsqrt);
                    rValue[3] = price * tsqrt * nd(d1) / 100;
                    rValue[4] = (-(price * v * nd(d1)) / (2 * tsqrt) + r * strike * Math.Exp(-r * t) * Cnd(d2)) / 365;
                }
            }
            else
            {
                if (optionType == "c")
                {
                    rValue[0] = Math.Max(price - strike, 0);
                    rValue[1] = price - strike > 0 ? 1 : 0;
                    rValue[2] = 0;
                    rValue[3] = 0;
                    rValue[4] = 0;
                }
                else 
                {
                    rValue[0] = Math.Max(strike - price, 0);
                    rValue[1] = strike - price > 0 ? -1 : 0;
                    rValue[2] = 0;
                    rValue[3] = 0;
                    rValue[4] = 0;
                }
            }

            return rValue;
        } // CalcPriceAndGreeks

        public static double[] OptionInnerAndTimeValue(string optionType, double price, double strike, double baseprice)
        {
            double InnerValue;
            if (optionType == "c")
            {
                InnerValue = Math.Max(0, baseprice - strike);
                return new double[] { InnerValue, Math.Abs(InnerValue - price) };
            }
            InnerValue = Math.Max(0, strike - baseprice);
            return new double[] { InnerValue, Math.Abs(InnerValue - price) };
        }
        
        public static double OptionInnerValue(string optionType, double strike, double baseprice)
        {
            if (optionType == "c") return Math.Max(0, baseprice - strike);
            return Math.Max(0, strike - baseprice);
        }


        private static double half(double x)
        {
            double y;
            if (x <= 0.5)
            {
                y = x;
            }
            else
            {
                y = (1.0 - x);
            }
            return Math.Pow(y * 2, 1.32);
        }

        private static double Cnd(double x)
        //'accurate to 1.e-15, according to J. Hart, cf. Graeme West
        {
            double absx;
            double frac;
            double c = 0.0;

            absx = Math.Abs(x);

            if (absx <= 37.0)
            {
                if (absx < 7.07106781186547)
                {
                    c = Math.Exp(-absx * absx / 2) *
                      ((((((3.52624965998911E-02 * absx
                            + 0.700383064443688) * absx
                          + 6.37396220353165) * absx
                          + 33.912866078383) * absx
                          + 112.079291497871) * absx
                          + 221.213596169931) * absx
                          + 220.206867912376)
                      /
                      (((((((8.83883476483184E-02 * absx
                          + 1.75566716318264) * absx
                          + 16.064177579207) * absx
                          + 86.7807322029461) * absx
                          + 296.564248779674) * absx
                          + 637.333633378831) * absx
                          + 793.826512519948) * absx
                          + 440.413735824752);
                }
                else
                {
                    frac = 4.0 / (absx + 0.65);
                    frac = 3.0 / (absx + frac);
                    frac = 2.0 / (absx + frac);
                    frac = 1.0 / (absx + frac);
                    c = Math.Exp(-absx * absx * 0.5) * 0.398942280401433 / (absx + frac);
                    //invsqrt2pi = 0.398942280401433 ' = 1/sqrt(2*PI)
                }
            }

            //'If 0# < x Then cdfN = 1# - cdfN
            c = (x > 0) ? 1 - c : c;
            return c;
        }

        private static double Cnd4(double x)
        {
            if ((x > 0) || (x < 0))
            {
                return alglib.normaldistribution(x);
            }
            else return 0;

            //double y = alglib.normaldistribution(x);
            //if (y < 0) x = 1 - x;
            //return y;
        }

        private static double Cnd2(double x)
        {
            double l;
            double k;
            double cnd;

            // Taylor series coefficients
            double a1 = 0.31938153;
            double a2 = -0.356563782;
            double a3 = 1.781477937;
            double a4 = -1.821255978;
            double a5 = 1.330274429;
            l = Math.Abs(x);
            k = 1.0 / (1.0 + 0.2316419 * l);
            cnd = 1.0 - 1.0 / 2.506628274631 * Math.Exp(-l * l / 2.0) * (a1 * k + a2 * k * k + a3 * k * k * k + a4 * k * k * k * k + a5 * k * k * k * k * k);

            if (x < 0)
            {
                return 1.0 - cnd;
            }
            else
            {
                return cnd;
            }
        } // Cnd

        private static double nd(double x)
        {
            return Math.Exp(-(Math.Pow((x) / 1.0, 2.0) / 2.0)) / Math.Sqrt(2 * Math.PI) / 1.0;
        }

        public static double[] CalcTimeBetweenDates(DateTime start, DateTime end)
        {
            if (end <= start) return new double[] { 0, 0, 0 };
            DateTime endDay = new DateTime(end.Year, end.Month, end.Day);
            DateTime startDay = new DateTime(start.Year, start.Month, start.Day);
            int businessDays = 0;
            int nights = 0;
            int weekends = 0;
            while (true)
            {
                if (Math.Floor((endDay - startDay).TotalDays) == 0) break;
                startDay = startDay.AddDays(1);
                nights++;
                if ((startDay.DayOfWeek != DayOfWeek.Sunday) && (startDay.DayOfWeek != DayOfWeek.Saturday))
                { businessDays++; }
                else { weekends++; };
            }
            return new double[] { businessDays, nights, weekends };
        }

        public static double CalcDailyRFactor(DateTime start, DateTime end, double nightKoef, double weekendKoef, Dictionary<UInt64, double> intradayTrajectory)
        {
            weekendKoef = (weekendKoef * 2 - nightKoef) / 2;
            double weightsSum = 0;
            DateTime endDay = new DateTime(end.Year, end.Month, end.Day);
            DateTime startDay = new DateTime(start.Year, start.Month, start.Day);
            while (true)
            {
                if (Math.Floor((endDay - startDay).TotalDays) == 0) break;
                startDay = startDay.AddDays(1);
                if ((startDay.DayOfWeek != DayOfWeek.Sunday) && (startDay.DayOfWeek != DayOfWeek.Saturday))
                {
                    weightsSum += 1.0;
                }
                else
                {
                    weightsSum += weekendKoef;
                };
            }

            UInt64 startIntradayTimeIndex = (UInt64)(start.Hour * 60 + start.Minute);
            if (startIntradayTimeIndex < 600) startIntradayTimeIndex = 600;
            if (startIntradayTimeIndex > 1430) startIntradayTimeIndex = 1430;
            UInt64 endIntradayTimeIndex = (UInt64)(end.Hour * 60 + end.Minute);
            if (endIntradayTimeIndex < 600) endIntradayTimeIndex = 600;
            if (endIntradayTimeIndex > 1430) endIntradayTimeIndex = 1430;

            double timeOfStartDayKoef = intradayTrajectory[startIntradayTimeIndex];  // остаток текущего дня
            double timeOfEndDayKoef = intradayTrajectory[endIntradayTimeIndex];  // остаток последнего дня

            if ((start.DayOfWeek != DayOfWeek.Sunday) && (start.DayOfWeek != DayOfWeek.Saturday))
                weightsSum = weightsSum + (1.0 - nightKoef) * (1 - timeOfStartDayKoef);

            weightsSum = weightsSum - (1.0 - nightKoef) * (1 - timeOfEndDayKoef);

            return weightsSum;
        }

        public static double Part_Of_Year_To_Date(string Date)
        {
            double Time = 0;
            try
            { Time = ((DateTime.Parse(Date) - DateTime.Now).TotalSeconds + 67500) / (365 * 24 * 60 * 60); }
            catch { Time = 0; }

            if (Time > 0)
                return Time;
            else
                return 0;
        }

        public static double From_Price_To_Volatility(double Strike, string Type, string ExpirationDate, double Price, double FuturesPrice)
        {
            double zLeft = 0.00001, zRight = 5;
            double z = zLeft;
            int n = 15;
            double T = Part_Of_Year_To_Date(ExpirationDate);

            if (Price != 0)
            {
                if (Type == "Call")
                {
                    for (int i = 0; i < n; i++)
                    {
                        z = (zLeft + zRight) / 2;
                        double Volatility = z;
                        double d1 = (Math.Log(FuturesPrice / Strike) + Volatility * Volatility * T / 2) / (Volatility * Math.Sqrt(T));
                        if (Math.Round(FuturesPrice * alglib.normaldistr.normaldistribution(d1) - Strike * alglib.normaldistr.normaldistribution(d1 - Volatility * Math.Sqrt(T)), 2) > Price)
                            zRight = z;
                        else
                            zLeft = z;
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        z = (zLeft + zRight) / 2;
                        double Volatility = z;
                        double d1 = (Math.Log(FuturesPrice / Strike) + Volatility * Volatility * T / 2) / (Volatility * Math.Sqrt(T));
                        if (Math.Round(-FuturesPrice * alglib.normaldistr.normaldistribution(-d1) + Strike * alglib.normaldistr.normaldistribution(-d1 + Volatility * Math.Sqrt(T)), 2) > Price)
                            zRight = z;
                        else
                            zLeft = z;
                    }
                }
                return (zLeft + zRight) / 2;
            }
            else
                return 0;
        }
    }
}