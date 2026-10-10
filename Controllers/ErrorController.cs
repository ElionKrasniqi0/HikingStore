using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using HikingStore.Models;

namespace HikingStore.Controllers
{
    public class ErrorController : Controller
    {
        private readonly ILogger<ErrorController> _logger;

        public ErrorController(ILogger<ErrorController> logger)
        {
            _logger = logger;
        }

        [Route("/Error/{statusCode}")]
        public IActionResult HttpStatusCodeHandler(int statusCode)
        {
            var reExecute = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
            var originalPath = reExecute == null ? "unknown" : reExecute.OriginalPath + reExecute.OriginalQueryString;

            switch (statusCode)
            {
                case 404:
                    ViewBag.ErrorMessage = "Sorry, the page you're looking for cannot be found.";
                    ViewBag.ErrorCode = "404 - Page Not Found";
                    _logger.LogWarning("404 error occurred. Path: {Path}", originalPath);
                    return View("NotFound");
                case 500:
                    ViewBag.ErrorMessage = "Sorry, something went wrong on our end. We're working to fix it.";
                    ViewBag.ErrorCode = "500 - Internal Server Error";
                    break;
                case 403:
                    ViewBag.ErrorMessage = "Sorry, you don't have permission to access this resource.";
                    ViewBag.ErrorCode = "403 - Forbidden";
                    break;
                case 401:
                    ViewBag.ErrorMessage = "Sorry, you need to be authenticated to access this resource.";
                    ViewBag.ErrorCode = "401 - Unauthorized";
                    break;
                default:
                    ViewBag.ErrorMessage = "Sorry, an error occurred while processing your request.";
                    ViewBag.ErrorCode = $"{statusCode} - Error";
                    break;
            }

            return View("Error");
        }

        [Route("/Error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var exceptionHandlerPathFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

            // Default error message
            ViewBag.ErrorMessage = "Sorry, an unexpected error occurred.";
            ViewBag.ErrorCode = "500 - Internal Server Error";

            if (exceptionHandlerPathFeature != null)
            {
                var exception = exceptionHandlerPathFeature.Error;
                var path = exceptionHandlerPathFeature.Path;

                _logger.LogError(exception, "An unhandled exception occurred at {Path}", path);

                // In development, show more details
                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    ViewBag.ExceptionPath = path;
                    ViewBag.ExceptionMessage = exception.Message;
                    ViewBag.StackTrace = exception.StackTrace;
                    ViewBag.ExceptionType = exception.GetType().Name;
                }
            }

            return View("Error");
        }

        // Specific error pages
        [Route("/Error/NotFound")]
        public IActionResult NotFoundError()
        {
            ViewBag.ErrorMessage = "Sorry, the page you're looking for cannot be found.";
            ViewBag.ErrorCode = "404 - Page Not Found";
            Response.StatusCode = 404;
            return View("NotFound");
        }

        [Route("/Error/AccessDenied")]
        public IActionResult AccessDenied()
        {
            ViewBag.ErrorMessage = "Sorry, you don't have permission to access this resource.";
            ViewBag.ErrorCode = "403 - Access Denied";
            Response.StatusCode = 403;
            return View("Error");
        }
    }
}