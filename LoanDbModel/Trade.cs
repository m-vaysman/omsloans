namespace LoanDbModel
{
    using Microsoft.EntityFrameworkCore;
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel.DataAnnotations;

    public class Trade
    {
        public Collection<Paydown> Paydowns { get; set; }
        public Collection<TradeDocument> TradeDocuments { get; set; }
        public Collection<Accrual> Accruals { get; set; }
        public int TradeId { get; set; }
        public CounterParty CounterParty { get; set; }
        public int CounterPartyId { get; set; }

        [Required]
        public string Ticker { get; set; }

        public string CUSIP { get; set; }

        [Required]
        public DateTime TradeDate { get; set; }

        [Required]
        public DateTime SettlementDate { get; set; }

        [Required]
        public string BuySell { get; set; }

        [Precision(18, 2)]
        [Required]
        public Decimal Price { get; set; }

        public decimal GlobalCommitment { get; set; }

        [Precision(18, 2)]
        [Required]
        public decimal Notional { get; set; }

        [Precision(18, 2)]
        public decimal TermUnfundedAmount { get; set; }

        [Precision(18, 2)]
        public decimal CommitmentReduction { get; set; }

        [Precision(18, 2)]
        public decimal FeesCosts { get; set; }

        [Precision(18, 2)]
        public decimal DelayCompensation { get; set; }

        [Precision(18, 2)]
        public decimal InterestReceived { get; set; }

        [Precision(18, 2)]
        public decimal AdditionalCredit { get; set; }

        [Precision(18, 2)]
        public decimal EconomicBenefit { get; set; }

        [Required]
        public string TradeAcct { get; set; }

        public string? Strategy { get; set; }

        public string? SubStrategy { get; set; }

        public string Currency { get; set; }

        [Precision(18, 2)]
        [Required]
        public decimal Spread { get; set; }

        public bool IsSettled { get; set; }

    }

}