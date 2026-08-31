using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace AgenticWorkflowPoC.Plugins.Data
{
    public class TransactionalSqlPlugin
    {
        [KernelFunction( "GetCustomerTransactionHistory" )]
        [Description( "Gets the recent transaction history of a customer." )]
        public Task<string> GetCustomerTransactionHistoryAsync( string customerId, int days )
        {
            return Task.FromResult( "No recent transactions." );
        }
    }
}
