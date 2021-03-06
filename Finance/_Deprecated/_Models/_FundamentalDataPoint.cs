﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.Entity;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Finance.Models
{
    public partial class FundamentalDataPoint : IEquatable<FundamentalDataPoint>
    {
        [Key]
        public int Id { get; set; }

        public virtual Security Security { get; set; }

        public string DataPointId { get; set; }

        public decimal DataPointValue { get; set; }

        public override bool Equals(object obj)
        {
            return Equals(obj as FundamentalDataPoint);
        }

        public bool Equals(FundamentalDataPoint other)
        {
            return other != null &&
                   EqualityComparer<Security>.Default.Equals(Security, other.Security) &&
                   DataPointId == other.DataPointId;
        }

        public override int GetHashCode()
        {
            var hashCode = -86004105;
            hashCode = hashCode * -1521134295 + EqualityComparer<Security>.Default.GetHashCode(Security);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(DataPointId);
            return hashCode;
        }

        public static bool operator ==(FundamentalDataPoint point1, FundamentalDataPoint point2)
        {
            return EqualityComparer<FundamentalDataPoint>.Default.Equals(point1, point2);
        }

        public static bool operator !=(FundamentalDataPoint point1, FundamentalDataPoint point2)
        {
            return !(point1 == point2);
        }
    }
}
