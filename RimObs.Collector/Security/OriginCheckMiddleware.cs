using RimWorks.RimObs.Collector.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace RimWorks.RimObs.Collector.Security;

internal static class OriginCheckMiddleware {
    public static IApplicationBuilder UseOriginCheck(this IApplicationBuilder app, int port) {
        return app.Use(async (HttpContext ctx, RequestDelegate next) => {
            string method = ctx.Request.Method;
            ConfigStore? configStore = ctx.RequestServices.GetService<ConfigStore>();
            bool csrfEnabled = configStore?.Current.Security.CsrfOriginCheckEnabled ?? true;
            if (!OriginCheck.ShouldEnforce(method, csrfEnabled)) {
                await next(ctx);
                return;
            }

            string? origin = ctx.Request.Headers.Origin.Count > 0
                ? ctx.Request.Headers.Origin.ToString()
                : null;
            if (!OriginCheck.IsAllowedOrigin(origin, port)) {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Forbidden: Origin header required for state-changing requests.");
                return;
            }

            await next(ctx);
        });
    }
}
