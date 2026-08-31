using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace AgenticWorkflowPoC.Plugins.Financial
{
    public class PaymentRiskPlugin
    {
        [KernelFunction( "GetAccountRiskLimit" )]
        [Description( "Gets the transactional risk limit for an account." )]
        public Task<decimal> GetAccountRiskLimitAsync( string accountId )
        {
            return Task.FromResult( 15000.00m );
        }
    }
}