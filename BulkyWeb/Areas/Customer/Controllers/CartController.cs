using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Globalization;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailSender _emailSender;
        // Will automatically update from view when using BindProperty
        [BindProperty]
        public ShoppingCartVM ShoppingCartVM { get; set; }

        public CartController(IUnitOfWork unitOfWork, IEmailSender emailSender)
        {
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
        }

        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(
                    u => u.ApplicationUserId == userId,
                    includeProperties: "Product"
                ),
                OrderHeader = new()
            };

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
            {
                cart.Price = GetPriceBasedOnQuantity(cart);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }

            return View(ShoppingCartVM);
        }

        public IActionResult Summary()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(
                    u => u.ApplicationUserId == userId,
                    includeProperties: "Product"
                ),
                OrderHeader = new()
            };

            ShoppingCartVM.OrderHeader.ApplicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);

            // find info from ApplicationUser, then display on page
            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;
            ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State;
            ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode;

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
            {
                cart.Price = GetPriceBasedOnQuantity(cart);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }

            return View(ShoppingCartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
        public IActionResult SummaryPOST()
        {
            // We added bindProperty, so no need to fill ShoppingCartVM in SummaryPOST()
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM.ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(
                    u => u.ApplicationUserId == userId,
                    includeProperties: "Product");

            // Be careful here, can compare to Summary()
            // Originally is ShoppingCartVM.OrderHeader.ApplicationUser, this navigation property 
            // will be populated and conflict with ApplicationUser database when add new OrderHeader
            ApplicationUser applicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
            {
                cart.Price = GetPriceBasedOnQuantity(cart);
                ShoppingCartVM.OrderHeader.OrderTotal += (cart.Price * cart.Count);
            }

			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
				// It is a regular customer account 
				ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
				ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
			}
			else
			{
				// It is a company user
				ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
				ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
			}

            // I added it by myself
            ShoppingCartVM.OrderHeader.ApplicationUserId = userId;

            _unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
			_unitOfWork.Save();

			foreach (var cart in ShoppingCartVM.ShoppingCartList)
			{
				OrderDetail orderDetail = new()
				{
					ProductId = cart.ProductId,
					OrderHeaderId = ShoppingCartVM.OrderHeader.Id,
					Price = cart.Price,
					Count = cart.Count
				};
				_unitOfWork.OrderDetail.Add(orderDetail);
			}
			_unitOfWork.Save();

            if (applicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                // It is a regular customer account and we need to capture payment
                // stripe logic
                var domain = Request.Scheme + "://" + Request.Host.Value + "/";
                var options = new SessionCreateOptions
                {
                    SuccessUrl = domain + $"customer/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}", // 支付成功後重定向的 URL
                    CancelUrl = domain + "customer/cart/index",
                    LineItems = new List<SessionLineItemOptions>(),
                    Mode = "payment", // 模式設置為 "payment" 用於完成一次性支付
                };

                var currencyCode = GetCurrencyCode();

                foreach (var item in ShoppingCartVM.ShoppingCartList)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(item.Price * 100), // 將價格轉換為以最小貨幣單位計算 (如美元的美分)，$20.50 會轉換為 2050
                            Currency = currencyCode, // Set currency unit dynamically
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Product.Title // 產品名稱將顯示在 Checkout 頁面上
                            }
                        },
                        Quantity = item.Count // 設置購買的數量
                    };

                    options.LineItems.Add(sessionLineItem); // 將每個購物車項目添加到 Checkout 會話的 LineItems 列表中
                }


                var service = new SessionService(); // 創建一個 SessionService 來處理會話
                Session session = service.Create(options); // 創建會話，並使用 options 來定義會話參數
                _unitOfWork.OrderHeader.UpdateStripePaymentID(ShoppingCartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
                _unitOfWork.Save();
                Response.Headers.Add("Location", session.Url); // 在響應標頭中添加重定向的 URL
                return new StatusCodeResult(303);              // 返回狀態碼 303，指示用戶端進行重定向

            }

            return RedirectToAction(nameof(OrderConfirmation), new { id = ShoppingCartVM.OrderHeader.Id});
        }

        public IActionResult OrderConfirmation(int id)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(u => u.Id == id, includeProperties: "ApplicationUser");
            if (orderHeader.PaymentStatus != SD.PaymentStatusDelayedPayment)
            {
                // 如果支付狀態不是延遲支付，則處理此訂單（假設為客戶訂單）
                var service = new SessionService();  // 建立與 Stripe 服務的連接
                Session session = service.Get(orderHeader.SessionId);  // 使用 orderHeader 中的 sessionId 從 Stripe 獲取會話

                if (session.PaymentStatus.ToLower() == "paid")
                {
                    // 如果支付狀態是 "paid"，則更新支付相關信息
                    _unitOfWork.OrderHeader.UpdateStripePaymentID(id, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderHeader.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
                    _unitOfWork.Save();  // 保存更改
                }
                HttpContext.Session.Clear();
            }

            // When make a new order, system will send email
            _emailSender.SendEmailAsync(orderHeader.ApplicationUser.Email, "New Order - BulkyWeb",
                $"<p>New Order Created - {orderHeader.Id}</p>");

            List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCart
                .GetAll(u => u.ApplicationUserId == orderHeader.ApplicationUserId).ToList();

            _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
            _unitOfWork.Save();

            return View(id);

        }



        public IActionResult Plus(int cartId)
        {
            var cartFromDb = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId);
            cartFromDb.Count += 1;
            _unitOfWork.ShoppingCart.Update(cartFromDb);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Minus(int cartId)
        {
            // If no tracked, page will has error
            var cartFromDb = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId, tracked: true);
            if (cartFromDb.Count <= 1)
            {
                HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart
                    .GetAll(u => u.ApplicationUserId == cartFromDb.ApplicationUserId).Count()-1);
                // remove that from cart
                _unitOfWork.ShoppingCart.Remove(cartFromDb);
            }
            else
            {
                cartFromDb.Count -= 1;
                _unitOfWork.ShoppingCart.Update(cartFromDb);
            }

            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Remove(int cartId)
        {
            // If no tracked, page will has error
            var cartFromDb = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId, tracked: true);
            HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart
                .GetAll(u => u.ApplicationUserId == cartFromDb.ApplicationUserId).Count()-1);
            // remove that from cart
            _unitOfWork.ShoppingCart.Remove(cartFromDb);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }


        private double GetPriceBasedOnQuantity(ShoppingCart shoppingCart)
        {
            if (shoppingCart.Count <= 50)
            {
                return shoppingCart.Product.Price;
            }
            else
            {
                if (shoppingCart.Count <= 100)
                {
                    return shoppingCart.Product.Price50;
                }
                else
                {
                    return shoppingCart.Product.Price100;
                }
            }
        }

        public string GetCurrencyCode()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                // 獲取當前的區域設置
                var region = new RegionInfo(culture.Name);
                // 使用 RegionInfo 獲取 ISO 貨幣代碼
                return region.ISOCurrencySymbol;
            }
            catch
            {
                return "USD"; // 預設為美元
            }
        }


    }
}
