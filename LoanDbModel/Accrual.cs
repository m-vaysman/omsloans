using Microsoft.EntityFrameworkCore;

namespace LoanDbModel
{
    public class Accrual
    {
        public int AccrualId { get; set; }
        public int? ParentAccrualId { get; set; }
        public string AccrualCode { get; set; } //computed col
        public ICollection<Accrual> ChildAccruals { get; set; }
        public Accrual ParentAccrual { get; set; }
        public Trade Trade { get; set; }
        public int TradeId { get; set; }
        public decimal Notional { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        [Precision(18, 6)]
        public decimal BankRate { get; set; }

        [Precision(18, 6)]
        public decimal Spread { get; set; }
        public string Act { get; set; }
        public string? Note { get; set; }
        
    }
}