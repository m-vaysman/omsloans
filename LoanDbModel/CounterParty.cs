using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LoanDbModel
{
    public class CounterParty
    {
        public Collection<Blotter> Blotters { get; set; }
        [Column(TypeName = "varchar(50)")]
        public string CounterPartyCode { get; set; }
        [Key]
        public int CounterPartyId { get; set; }
        [Column(TypeName = "varchar(500)")]
        public string CounterPartyName { get; set; }
        [Column(TypeName = "varchar(50)")]
        public string DomicileCountry { get; set; }
        public Collection<Trade> Trades { get; set; }
    }
}
