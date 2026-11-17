using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgenticWorkflowPoC.Api.Security
{
    public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ApiKey";
        public const string ApiKeyHeaderName = "X-Api-Key";
        public const string TenantClaimType = "tenant_id";

        private readonly IConfiguration _configuration;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration)
            : base(options, logger, encoder)
        {
            _configuration = configuration;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
                || string.IsNullOrWhiteSpace(providedKey))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey.ToString()));
            foreach (var configuredKey in _configuration.GetSection("Authentication:ApiKeys").GetChildren())
            {
                var expectedKey = configuredKey["Key"];
                var tenantId = configuredKey["TenantId"];
                if (string.IsNullOrWhiteSpace(expectedKey)
                    || string.IsNullOrWhiteSpace(tenantId)
                    || tenantId.Length > 100)
                {
                    continue;
                }

                var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));
                if (!CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
                {
                    continue;
                }

                var claims = new List<Claim>
                {
                    new(TenantClaimType, tenantId),
                    new(ClaimTypes.NameIdentifier, configuredKey["Subject"] ?? tenantId)
                };

                var role = configuredKey["Role"];
                if (!string.IsNullOrWhiteSpace(role))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                var identity = new ClaimsIdentity(claims, SchemeName);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, SchemeName);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }
    }
}
