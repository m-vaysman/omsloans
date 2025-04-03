using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LoanDbModel
{
    public class Paydown
    {
        public int PaydownId { get; set; }
        public Collection<SettledPaydownCashWire> SettledPaydownCashWires { get; set; }

        public Trade Trade { get; set; }

        public int TradeId { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public string? Note { get; set; }

        [Precision(18, 2)]
        [Required]
        public decimal Amount { get; set; }

        [Column(TypeName = "varchar(500)")]
        public string? Notice { get; set; }

        [Precision(18, 2)]
        public decimal? ProRataShare { get; set; } // Stored as string e.g. "0.88%"

    }
}