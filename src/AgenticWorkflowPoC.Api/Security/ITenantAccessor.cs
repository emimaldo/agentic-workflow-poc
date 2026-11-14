using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AgenticWorkflowPoC.Api.Security
{
    /// <summary>
    /// Scoped accessor for the authenticated tenant context (Chapter 2).
    /// The tenant is resolved exclusively from the authenticated principal, never from the prompt or body.
    /// </summary>
    public interface ITenantAccessor
    {
        string GetCurrentTenantId();
    }

    public sealed class TenantContextUnavailableException : InvalidOperationException
    {
        public TenantContextUnavailableException()
            : base("The request has no authenticated tenant context.")
        {
        }
    }

    public sealed class HttpContextTenantAccessor : ITenantAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextTenantAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetCurrentTenantId()
        {
            var tenantId = _httpContextAccessor.HttpContext?.User
                .FindFirstValue(ApiKeyAuthenticationHandler.TenantClaimType);

            // Fail-closed: never fall back to a default tenant.
            return string.IsNullOrWhiteSpace(tenantId)
                ? throw new TenantContextUnavailableException()
                : tenantId;
        }
    }
}
