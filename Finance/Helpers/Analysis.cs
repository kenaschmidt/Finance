﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Finance.Calendar;

namespace Finance
{
    public static class Analysis
    {
        public static List<NetChangeByTrendType> GetNetChangeByTrendType(this Security me, PriceBarSize priceBarSize, int barCount, DateTime? start = null, DateTime? end = null)
        {
            List<PriceBar> PriceBarsUsed;

            switch (priceBarSize)
            {

                case PriceBarSize.Weekly:
                    {
                        PriceBarsUsed = me.WeeklyPriceBarData;
                        if (!start.HasValue)
                        {
                            start = me.GetFirstBar(PriceBarSize.Weekly).BarDateTime;
                            end = me.GetLastBar(PriceBarSize.Weekly).BarDateTime;
                        }
                        if (start.HasValue && !end.HasValue)
                            throw new UnknownErrorException() { message = "Invalid input dates" };
                        start = FirstTradingDayOfWeek(start.Value);
                        end = FirstTradingDayOfWeek(end.Value);
                    }
                    break;
                case PriceBarSize.Monthly:
                    {
                        PriceBarsUsed = me.MonthlyPriceBarData;
                        if (!start.HasValue)
                        {
                            start = me.GetFirstBar(PriceBarSize.Monthly).BarDateTime;
                            end = me.GetLastBar(PriceBarSize.Monthly).BarDateTime;
                        }
                        if (start.HasValue && !end.HasValue)
                            throw new UnknownErrorException() { message = "Invalid input dates" };
                        start = FirstTradingDayOfMonth(start.Value);
                        end = FirstTradingDayOfMonth(end.Value);
                    }
                    break;
                case PriceBarSize.Quarterly:
                    {
                        PriceBarsUsed = me.QuarterlyPriceBarData;
                        if (!start.HasValue)
                        {
                            start = me.GetFirstBar(PriceBarSize.Quarterly).BarDateTime;
                            end = me.GetLastBar(PriceBarSize.Quarterly).BarDateTime;
                        }
                        if (start.HasValue && !end.HasValue)
                            throw new UnknownErrorException() { message = "Invalid input dates" };
                        start = FirstTradingDayOfQuarter(start.Value);
                        end = FirstTradingDayOfQuarter(end.Value);
                    }
                    break;
                case PriceBarSize.Daily:
                default:
                    {
                        PriceBarsUsed = me.DailyPriceBarData;
                        if (!start.HasValue)
                        {
                            start = me.GetFirstBar(PriceBarSize.Daily).BarDateTime;
                            end = me.GetLastBar(PriceBarSize.Daily).BarDateTime;
                        }
                        if (start.HasValue && !end.HasValue)
                            throw new UnknownErrorException() { message = "Invalid input dates" };
                    }
                    break;
            }


            if (PriceBarsUsed.Count == 0)
                return null;

            // Create a return list of all trends except NotSet
            var ret = new List<NetChangeByTrendType>();
            foreach (var trend in Enum.GetValues(typeof(TrendQualification)))
                if ((TrendQualification)trend != TrendQualification.NotSet)
                    ret.Add(new Analysis.NetChangeByTrendType((TrendQualification)trend));

            // Set swingpoints and trends if not done already
            me.SetSwingPointsAndTrends(barCount, priceBarSize);

            // Get the first bar, ignore the starting ambilavent trend
            PriceBar currentBar = me.GetPriceBar(start.Value, priceBarSize);

            // Track the current trend as we go through each bar
            TrendQualification currentTrend = currentBar.GetTrendType(barCount);

            while (currentBar != null && currentBar.GetTrendType(barCount) == currentTrend)
                currentBar = currentBar.NextBar;

            PriceBar firstBarOfTrend = null;
            PriceBar lastBarOfTrend = null;

            while (currentBar != null)
            {
                // Since the trend change is based on the ending value for a bar, we track a trend from the second instance through the first bar of the next trend
                if (currentTrend != currentBar.GetTrendType(barCount))
                {
                    lastBarOfTrend = currentBar;
                    if (firstBarOfTrend != null)
                    {
                        var trendResult = ret.Find(x => x.TrendType == currentTrend);
                        trendResult.AddValues(firstBarOfTrend, lastBarOfTrend);
                    }

                    firstBarOfTrend = currentBar.NextBar;
                    currentTrend = currentBar.GetTrendType(barCount);
                }
                currentBar = currentBar.NextBar;
            }

            return ret;
        }
        public class NetChangeByTrendType
        {
            public TrendQualification TrendType { get; }
            public int NumberOccurrance { get; set; } = 0;
            public decimal AverageChange { get; set; } = 0;

            public void AddValues(PriceBar firstBarOfTrend, PriceBar lastBarOfTrend)
            {
                if (firstBarOfTrend.Open == 0)
                    return;

                var changePercent = (lastBarOfTrend.Close - firstBarOfTrend.Open) / firstBarOfTrend.Open;
                AverageChange = ((AverageChange * NumberOccurrance) + changePercent) / ++NumberOccurrance;
            }

            public NetChangeByTrendType(TrendQualification trendType)
            {
                this.TrendType = trendType;
            }
        }

    }

    public static class Candlesticks
    {
        public static void SetCandlestickPatterns(this Security me)
        {
            var methods = (from m in typeof(Candlesticks).GetTypeInfo().GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                           where Attribute.IsDefined(m, typeof(IncludeAttribute))
                           select m).ToList();

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<IncludeAttribute>().Include)
                {
                    foreach (PriceBar bar in me.GetPriceBars(PriceBarSize.Daily))
                        method.Invoke(null, new[] { bar });
                }
            }
        }

        [Include(true)]
        private static void Candlestick_Hammer(this PriceBar me)
        {
            decimal wickToBody = 2.0m;
            decimal upperWickLimit = .25m;

            // Bottom of a down trend, with a lower wick at least 2x body, closing at or near high

            // Define downtrend using SP bar count
            var priorBars = me.PriorBars(3, false);
            if (priorBars.Count < 3)
            {
                me.SetCandlestickFlag(CandleStickPattern.BullishHammer, false);
                return;
            }

            if (priorBars.Min(x => x.Low) <= Math.Min(me.Open, me.Close))
            {
                me.SetCandlestickFlag(CandleStickPattern.BullishHammer, false);
                return;
            }

            var lowerWick = Math.Min(me.Open, me.Close) - me.Low;
            var upperWick = me.High - Math.Max(me.Open, me.Close);
            var body = Math.Abs(me.Change);

            if (lowerWick < 0)
            {
                me.SetCandlestickFlag(CandleStickPattern.BullishHammer, false);
                return;
            }
            if (body > 0 && lowerWick / body < wickToBody)
            {
                me.SetCandlestickFlag(CandleStickPattern.BullishHammer, false);
                return;
            }
            if (lowerWick < me.AverageTrueRange())
            {
                me.SetCandlestickFlag(CandleStickPattern.BullishHammer, false);
                return;
            }
            if (upperWick > body * upperWickLimit)
            {
                me.SetCandlestickFlag(CandleStickPattern.BullishHammer, false);
                return;
            }

            me.SetCandlestickFlag(CandleStickPattern.BullishHammer, true);
            return;

        }
    }

    public static class Technicals
    {
        public static void SetTechnicals(this Security me)
        {
            var methods = (from m in typeof(Technicals).GetTypeInfo().GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                           where Attribute.IsDefined(m, typeof(IncludeAttribute))
                           select m).ToList();

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<IncludeAttribute>().Include)
                {
                    foreach (PriceBar bar in me.GetPriceBars(PriceBarSize.Daily))
                        method.Invoke(null, new[] { bar });
                }
            }
        }

        [Include(true)]
        private static void Technical_Volume(this PriceBar me)
        {
            if (me.PriorBar == null)
                return;

            if (me.Volume > me.PriorBar.Volume)
            {
                me.SetTechnicalFlag(Technical.RisingVolume, true);
            }
            else if (me.Volume < me.PriorBar.Volume)
            {
                me.SetTechnicalFlag(Technical.FallingVolume, true);
            }
        }
    }

    public class PositionSummary
    {
        public List<Position> Positions { get; private set; }
        public Security Security { get; private set; }
        public string Ticker { get => Security?.Ticker; }

        public PositionSummary(Position position)
        {
            if (position == null)
                return;

            Security = position.Security;
            Positions = new List<Position>();
            Positions.Add(position);
        }
        public PositionSummary(List<Position> positions)
        {
            if (positions.Count == 0)
                return;

            Security = positions.FirstOrDefault().Security;
            Positions = new List<Position>();
            Positions.AddRange(positions);
        }

        public void Add(Position position)
        {
            if (position.Security != this.Security)
                throw new ArgumentException("Security mismatch");

            Positions.Add(position);
        }
        public void AddRange(List<Position> positions)
        {
            foreach (Position pos in positions)
                Add(pos);
        }

        #region Analytics

        public int TradeCount => (from pos in Positions select pos.ExecutedTrades.Count).Sum();
        public int DaysHeld => (from pos in Positions select pos.DaysHeld(Security.GetLastBar(PriceBarSize.Daily).BarDateTime)).Sum();
        public int PositionCount => Positions.Count;

        public decimal NetReturnDollars
        {
            get
            {
                return Positions.Sum(x => x.TotalReturnDollars(Security.GetLastBar(PriceBarSize.Daily).BarDateTime));
            }
        }
        public decimal NetReturnPercent
        {
            get
            {
                return Positions.Sum(x => x.TotalReturnPercentage(Security.GetLastBar(PriceBarSize.Daily).BarDateTime));
            }
        }
        public decimal NetReturnPerDayDollars
        {
            get
            {
                return (NetReturnDollars / DaysHeld);
            }
        }
        public decimal NetReturnPerDayPercent
        {
            get
            {
                return (NetReturnPercent / DaysHeld);
            }
        }
        public decimal AnnualizedNetReturnPercent
        {
            get
            {
                var returnPercent = Convert.ToDouble(NetReturnPercent);
                var dayHeldFraction = (365.0 / DaysHeld);

                return (Math.Pow((1 + returnPercent), (dayHeldFraction)) - 1).ToDecimal();
            }
        }

        #endregion

    }
}
