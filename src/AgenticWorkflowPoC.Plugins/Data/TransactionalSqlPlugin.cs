using System.Threading.Tasks;

namespace AgenticWorkflowPoC.Plugins.Data
{
    public class TransactionalSqlPlugin
    {
        public Task<string> GetCustomerTransactionHistoryAsync( string customerId, int days )
        {
            return Task.FromResult( "No recent transactions." );
        }
    }
}
