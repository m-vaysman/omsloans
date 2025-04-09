using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoanDbModel
{
    public class TradeDocument
    {
        public int TradeDocumentId { get; set; }
        public int TradeId { get; set; }
        public Trade Trade { get; set; }
        public string FileName { get; set; }

        public string ContentType { get; set; } // e.g., "application/pdf"

        public byte[] Data { get; set; } // This stores the binary content
    }
}
