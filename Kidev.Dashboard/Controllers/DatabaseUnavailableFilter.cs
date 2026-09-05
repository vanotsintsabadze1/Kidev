using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Kidev.Dashboard.Controllers;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by MVC dependency injection as a global exception filter.")]
internal sealed partial class DatabaseUnavailableFilter(ILogger<DatabaseUnavailableFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not (DbException or DbUpdateException or TimeoutException))
        {
            return;
        }

        LogDatabaseUnavailable(logger);
        context.Result = new ViewResult { ViewName = "Error", StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard database operation unavailable.")]
    private static partial void LogDatabaseUnavailable(ILogger logger);
}
