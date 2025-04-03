using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LoanDbModel
{
    public class Blotter
    {
        public int BlotterId { get; set; }

        [Column(TypeName = "varchar(1)")]
        [Required]
        public string BuySell { get; set; }

        public CounterParty CounterParty { get; set; }

        public int CounterPartyId { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string? CUSIP { get; set; }

        public string? Document { get; set; }

        [Precision(18, 2)]
        public decimal GlobalCommitment { get; set; }

        [Precision(18, 2)]
        [Required]
        public decimal Notional { get; set; }

        [Precision(18, 2)]
        [Required]
        public decimal Price { get; set; }

        [Precision(18, 2)]
        public decimal Spread { get; set; }

        [Column(TypeName = "varchar(50)")]
        [Required]
        public string Ticker { get; set; }

        public string? Ticket { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string TradeAcct { get; set; }

        [Required]
        public DateTime TradeDate { get; set; }
    }
}