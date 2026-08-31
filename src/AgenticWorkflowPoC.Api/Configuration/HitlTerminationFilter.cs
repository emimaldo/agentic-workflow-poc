using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace AgenticWorkflowPoC.Api.Configuration
{
    public class HitlTerminationFilter : IAutoFunctionInvocationFilter
    {
        public async Task OnAutoFunctionInvocationAsync( AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next )
        {
            await next( context );

            var resultString = context.Result.GetValue<string>();
            if( resultString != null && resultString.StartsWith( "HITL_PAUSE" ) )
            {
                context.Terminate = true;
            }
        }
    }
}
