using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WedNightFury.Filters
{
    public class AdminAuthorizeAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var role = context.HttpContext.Session.GetString("Role");

            if (string.IsNullOrEmpty(role) || role != "admin")
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
            }

            base.OnActionExecuting(context);
        }
    }
}
