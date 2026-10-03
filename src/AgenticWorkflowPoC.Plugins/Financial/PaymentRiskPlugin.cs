using System.Threading.Tasks;

namespace AgenticWorkflowPoC.Plugins.Financial
{
    public class PaymentRiskPlugin
    {
        public Task<decimal> GetAccountRiskLimitAsync( string accountId )
        {
            return Task.FromResult( 15000.00m );
        }
    }
}