using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Expressions = Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Expressions;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(ExpressionManager))]
    public interface IExpressionManager : IAgentService
    {
        Expressions.INode Parse(IExecutionContext context, string condition);
        bool Evaluate(IExecutionContext context, Expressions.INode tree, bool hostTracingOnly = false);
    }

    public sealed class ExpressionManager : AgentService, IExpressionManager
    {
        public static Expressions.INode Always = new AlwaysNode();
        public static Expressions.INode Succeeded = new SucceededNode();
        public static Expressions.INode SucceededOrFailed = new SucceededOrFailedNode();

        public Expressions.INode Parse(IExecutionContext executionContext, string condition)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            var expressionTrace = new TraceWriter(Trace, executionContext);
            var parser = new Expressions.Parser();
            var namedValues = new Expressions.INamedValueInfo[]
            {
                new Expressions.NamedValueInfo<VariablesNode>(name: Constants.Expressions.Variables),
            };
            var functions = new Expressions.IFunctionInfo[]
            {
                new Expressions.FunctionInfo<AlwaysNode>(name: Constants.Expressions.Always, minParameters: 0, maxParameters: 0),
                new Expressions.FunctionInfo<CanceledNode>(name: Constants.Expressions.Canceled, minParameters: 0, maxParameters: 0),
                new Expressions.FunctionInfo<FailedNode>(name: Constants.Expressions.Failed, minParameters: 0, maxParameters: 0),
                new Expressions.FunctionInfo<SucceededNode>(name: Constants.Expressions.Succeeded, minParameters: 0, maxParameters: 0),
                new Expressions.FunctionInfo<SucceededOrFailedNode>(name: Constants.Expressions.SucceededOrFailed, minParameters: 0, maxParameters: 0),
            };
            return parser.CreateTree(condition, expressionTrace, namedValues, functions) ?? new SucceededNode();
        }

        public bool Evaluate(IExecutionContext executionContext, Expressions.INode tree, bool hostTracingOnly = false)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNull(tree, nameof(tree));
            var expressionTrace = new TraceWriter(Trace, hostTracingOnly ? null : executionContext);
            return tree.EvaluateBoolean(trace: expressionTrace, state: executionContext);
        }

        private sealed class TraceWriter : Expressions.ITraceWriter
        {
            private readonly IExecutionContext _executionContext;
            private readonly Tracing _trace;

            public TraceWriter(Tracing trace, IExecutionContext executionContext)
            {
                ArgUtil.NotNull(trace, nameof(trace));
                _trace = trace;
                _executionContext = executionContext;
            }

            public void Info(string message)
            {
                _trace.Info(message);
                _executionContext?.Debug(message);
            }

            public void Verbose(string message)
            {
                _trace.Verbose(message);
                _executionContext?.Debug(message);
            }
        }

        private sealed class AlwaysNode : Expressions.FunctionNode
        {
            public sealed override string Name => Constants.Expressions.Always;

            protected sealed override object EvaluateCore(Expressions.EvaluationContext evaluationContext)
            {
                return true;
            }
        }

        private sealed class CanceledNode : Expressions.FunctionNode
        {
            public sealed override string Name => Constants.Expressions.Canceled;

            protected sealed override object EvaluateCore(Expressions.EvaluationContext evaluationContext)
            {
                var executionContext = evaluationContext.State as IExecutionContext;
                ArgUtil.NotNull(executionContext, nameof(executionContext));
                TaskResult jobStatus = executionContext.Variables.Agent_JobStatus ?? TaskResult.Succeeded;
                return jobStatus == TaskResult.Canceled;
            }
        }

        private sealed class FailedNode : Expressions.FunctionNode
        {
            public sealed override string Name => Constants.Expressions.Failed;

            protected sealed override object EvaluateCore(Expressions.EvaluationContext evaluationContext)
            {
                var executionContext = evaluationContext.State as IExecutionContext;
                ArgUtil.NotNull(executionContext, nameof(executionContext));
                TaskResult jobStatus = executionContext.Variables.Agent_JobStatus ?? TaskResult.Succeeded;
                return jobStatus == TaskResult.Failed;
            }
        }

        private sealed class SucceededNode : Expressions.FunctionNode
        {
            public sealed override string Name => Constants.Expressions.Succeeded;

            protected sealed override object EvaluateCore(Expressions.EvaluationContext evaluationContext)
            {
                var executionContext = evaluationContext.State as IExecutionContext;
                ArgUtil.NotNull(executionContext, nameof(executionContext));
                TaskResult jobStatus = executionContext.Variables.Agent_JobStatus ?? TaskResult.Succeeded;
                return jobStatus == TaskResult.Succeeded ||
                    jobStatus == TaskResult.SucceededWithIssues;
            }
        }

        private sealed class SucceededOrFailedNode : Expressions.FunctionNode
        {
            public sealed override string Name => Constants.Expressions.SucceededOrFailed;

            protected sealed override object EvaluateCore(Expressions.EvaluationContext evaluationContext)
            {
                var executionContext = evaluationContext.State as IExecutionContext;
                ArgUtil.NotNull(executionContext, nameof(executionContext));
                TaskResult jobStatus = executionContext.Variables.Agent_JobStatus ?? TaskResult.Succeeded;
                return jobStatus == TaskResult.Succeeded ||
                    jobStatus == TaskResult.SucceededWithIssues ||
                    jobStatus == TaskResult.Failed;
            }
        }

        private sealed class VariablesNode : Expressions.NamedValueNode
        {
            public sealed override string Name => Constants.Expressions.Variables;

            protected sealed override object EvaluateCore(Expressions.EvaluationContext evaluationContext)
            {
                var jobContext = evaluationContext.State as IExecutionContext;
                ArgUtil.NotNull(jobContext, nameof(jobContext));
                return new VariablesDictionary(jobContext.Variables);
            }
        }

        private sealed class VariablesDictionary : IReadOnlyDictionary<string, object>
        {
            private readonly Variables _variables;

            public VariablesDictionary(Variables variables)
            {
                _variables = variables;
            }

            // IReadOnlyDictionary<string object> members
            public object this[string key] => _variables.Get(key);

            public IEnumerable<string> Keys => throw new NotSupportedException();

            public IEnumerable<object> Values => throw new NotSupportedException();

            public bool ContainsKey(string key)
            {
                string val;
                return _variables.TryGetValue(key, out val);
            }

            public bool TryGetValue(string key, out object value)
            {
                string s;
                bool found = _variables.TryGetValue(key, out s);
                value = s;
                return found;
            }

            // IReadOnlyCollection<KeyValuePair<string, object>> members
            public int Count => throw new NotSupportedException();

            // IEnumerable<KeyValuePair<string, object>> members
            IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => throw new NotSupportedException();


            // IEnumerable members
            IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
        }
    }
}