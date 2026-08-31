using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace RimWorks.RimObs.Collector.Security;

internal static class BearerAuthMiddleware {
    public static IApplicationBuilder UseBearerAuth(this IApplicationBuilder app, CollectorToken token) {
        return app.Use(async (HttpContext ctx, RequestDelegate next) => {
            string method = ctx.Request.Method;
            if (!OriginCheck.RequiresCheck(method)) {
                await next(ctx);
                return;
            }

            string? authHeader = ctx.Request.Headers.Authorization.Count > 0
                ? ctx.Request.Headers.Authorization.ToString()
                : null;
            string? presented = BearerHeader.ExtractToken(authHeader);
            if (!token.Matches(presented)) {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate = "Bearer";
                await ctx.Response.WriteAsync("Unauthorized: Bearer token required for state-changing requests.");
                return;
            }

            await next(ctx);
        });
    }
}
