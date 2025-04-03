using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LoanDbModel
{
    public class SettledPaydownCashWire
    {

        [Precision(18, 2)]
        [Required]
        public decimal Amount { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public string Note { get; set; }
        public Paydown Paydown { get; set; }
        public int PaydownId { get; set; }
        [Key]
        public int SettledPaydownCashWireId { get; set; }
    }
}
