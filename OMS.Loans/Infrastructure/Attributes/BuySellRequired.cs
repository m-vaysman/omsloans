using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Infrastructure.Attributes
{
    using System;
    using System.ComponentModel.DataAnnotations;

    [AttributeUsage(AttributeTargets.Property|AttributeTargets.Field, AllowMultiple = false)]
    public class BuySellRequiredAttribute : ValidationAttribute
    {
        public bool IgnoreCase { get; set; } = false;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value is not string str || string.IsNullOrWhiteSpace(str))
                return new ValidationResult("Buy/Sell value is required.");

            if (IgnoreCase)
            {
                str = str.ToUpperInvariant();
            }

            if (str == "B" || str == "S")
                return ValidationResult.Success;

            return new ValidationResult("Value must be 'B' or 'S'.");
        }
    }

}
