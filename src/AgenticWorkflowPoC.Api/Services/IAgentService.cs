using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;

namespace AgenticWorkflowPoC.Api.Services
{
    public interface IAgentService
    {
        Task<AgentResponse> InvokeAsync(string sessionId, string prompt);
        Task<AgentResponse> ResumeAsync(string sessionId, bool isApproved);
    }
}
