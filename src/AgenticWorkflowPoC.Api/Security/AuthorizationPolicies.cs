namespace AgenticWorkflowPoC.Api.Security
{
    public static class AuthorizationPolicies
    {
        /// <summary>RBAC policy for HITL operators (Chapter 3).</summary>
        public const string RequireAdminRole = "RequireAdminRole";
    }
}
