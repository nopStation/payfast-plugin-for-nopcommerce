﻿using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.PayFast.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        [NopResourceDisplayName("Plugins.Payments.PayFast.Fields.MerchantId")]
        public string MerchantId { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayFast.Fields.MerchantKey")]
        public string MerchantKey { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayFast.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayFast.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }

        [NopResourceDisplayName("Plugins.Payments.PayFast.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
    }
}