using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.PayFast.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.PayFast.Controllers
{
    public class PaymentPayFastController : BasePaymentController
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly PayFastPaymentSettings _payFastPaymentSettings;
        private readonly INotificationService _notificationService;

        #endregion

        #region Ctor

        public PaymentPayFastController(ILocalizationService localizationService,
            ILogger logger,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPermissionService permissionService,
            ISettingService settingService,
            IWebHelper webHelper,
            PayFastPaymentSettings payFastPaymentSettings,
            INotificationService notificationService)
        {
            _localizationService = localizationService;
            _logger = logger;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _permissionService = permissionService;
            _settingService = settingService;
            _webHelper = webHelper;
            _payFastPaymentSettings = payFastPaymentSettings;
            _notificationService = notificationService;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Validate Instant Transaction Notification Callback
        /// </summary>
        /// <param name="form">List of parameters</param>
        /// <param name="order">Order</param>
        /// <returns>true if there are no errors; otherwise false</returns>
        protected async Task<(bool valid, Order order)> ValidateITNAsync(IFormCollection form)
        {
            Order order = null;

            //validate order
            if (!Guid.TryParse(form["m_payment_id"], out Guid orderGuid))
                return (false, order);

            order = await _orderService.GetOrderByGuidAsync(orderGuid);
            if (order == null)
            {
                await _logger.ErrorAsync($"PayFast ITN error: Order with guid {orderGuid} is not found");
                return (false, order);
            }

            //validate merchant ID
            if (!form["merchant_id"].ToString().Equals(_payFastPaymentSettings.MerchantId, StringComparison.InvariantCulture))
            {
                await _logger.ErrorAsync("PayFast ITN error: Merchant ID mismatch");
                return (false, order);
            }

            //validate IP address
            if (!IPAddress.TryParse(_webHelper.GetCurrentIpAddress(), out IPAddress ipAddress))
            {
                await _logger.ErrorAsync("PayFast ITN error: IP address is empty");
                return (false, order);
            }

            var validIPs = new[]
            {
                "www.payfast.co.za",
                "sandbox.payfast.co.za",
                "w1w.payfast.co.za",
                "w2w.payfast.co.za"
            }.SelectMany(Dns.GetHostAddresses);

            if (!validIPs.Contains(ipAddress))
            {
                await _logger.ErrorAsync($"PayFast ITN error: IP address {ipAddress} is not valid");
                return (false, order);
            }

            //validate data
            var postData = new NameValueCollection();

            foreach (var pair in form)
            {
                if (!pair.Key.Equals("signature", StringComparison.InvariantCultureIgnoreCase))
                {
                    postData.Add(pair.Key, pair.Value);
                }
            }

            try
            {
                var site = $"{(_payFastPaymentSettings.UseSandbox ? "https://sandbox.payfast.co.za" : "https://www.payfast.co.za")}/eng/query/validate";

                using (var webClient = new WebClient())
                {
                    var response = webClient.UploadValues(site, postData);

                    // Get the response and replace the line breaks with spaces
                    var result = Encoding.ASCII.GetString(response);

                    if (!result.StartsWith("VALID", StringComparison.InvariantCulture))
                    {
                        await _logger.ErrorAsync("PayFast ITN error: passed data is not valid");
                        return (false, order);
                    }
                }
            }
            catch (WebException)
            {
                await _logger.ErrorAsync("PayFast ITN error: passed data is not valid");
                return (false, order);
            }

            //validate payment status
            if (!form["payment_status"].ToString().Equals("COMPLETE", StringComparison.InvariantCulture))
            {
                await _logger.ErrorAsync($"PayFast ITN error: order #{order.Id} is {form["payment_status"]}");
                return (false, order);
            }

            return (true, order);
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                MerchantId = _payFastPaymentSettings.MerchantId,
                MerchantKey = _payFastPaymentSettings.MerchantKey,
                UseSandbox = _payFastPaymentSettings.UseSandbox,
                AdditionalFee = _payFastPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _payFastPaymentSettings.AdditionalFeePercentage
            };

            return View("~/Plugins/Payments.PayFast/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            _payFastPaymentSettings.MerchantId = model.MerchantId;
            _payFastPaymentSettings.MerchantKey = model.MerchantKey;
            _payFastPaymentSettings.UseSandbox = model.UseSandbox;
            _payFastPaymentSettings.AdditionalFee = model.AdditionalFee;
            _payFastPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            await _settingService.SaveSettingAsync(_payFastPaymentSettings);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return RedirectToAction("Configure");
        }

        public async Task<IActionResult> PayFastResultHandler(IFormCollection form)
        {
            //validation
            (bool valid, Order order) = await ValidateITNAsync(form);
            if (!valid)
                return new StatusCodeResult((int)HttpStatusCode.OK);

            //paid order
            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                order.AuthorizationTransactionId = form["pf_payment_id"];
                await _orderService.UpdateOrderAsync(order);
                await _orderProcessingService.MarkOrderAsPaidAsync(order);
            }

            return new StatusCodeResult((int)HttpStatusCode.OK);
        }

        #endregion
    }
}