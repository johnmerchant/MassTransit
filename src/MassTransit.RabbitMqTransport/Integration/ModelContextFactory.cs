﻿namespace MassTransit.RabbitMqTransport.Integration
{
    using System.Threading;
    using System.Threading.Tasks;
    using Context;
    using Contexts;
    using GreenPipes;
    using GreenPipes.Agents;
    using Internals.Extensions;
    using RabbitMQ.Client;


    public class ModelContextFactory :
        IPipeContextFactory<ModelContext>
    {
        readonly IConnectionContextSupervisor _supervisor;

        public ModelContextFactory(IConnectionContextSupervisor supervisor)
        {
            _supervisor = supervisor;
        }

        IPipeContextAgent<ModelContext> IPipeContextFactory<ModelContext>.CreateContext(ISupervisor supervisor)
        {
            IAsyncPipeContextAgent<ModelContext> asyncContext = supervisor.AddAsyncContext<ModelContext>();

            var context = CreateModel(asyncContext, supervisor.Stopped);

            void HandleShutdown(object sender, ShutdownEventArgs args)
            {
                if (args.Initiator != ShutdownInitiator.Application)
                    asyncContext.Stop(args.ReplyText);
            }

            context.ContinueWith(task =>
            {
                task.Result.Model.ModelShutdown += HandleShutdown;

                asyncContext.Completed.ContinueWith(_ => task.Result.Model.ModelShutdown -= HandleShutdown);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            return asyncContext;
        }

        IActivePipeContextAgent<ModelContext> IPipeContextFactory<ModelContext>.CreateActiveContext(ISupervisor supervisor,
            PipeContextHandle<ModelContext> context, CancellationToken cancellationToken)
        {
            return supervisor.AddActiveContext(context, CreateSharedModel(context.Context, cancellationToken));
        }

        async Task<ModelContext> CreateSharedModel(Task<ModelContext> context, CancellationToken cancellationToken)
        {
            var modelContext = await context.ConfigureAwait(false);

            return new ScopeModelContext(modelContext, cancellationToken);
        }

        Task<ModelContext> CreateModel(IAsyncPipeContextAgent<ModelContext> asyncContext, CancellationToken cancellationToken)
        {
            async Task<ModelContext> CreateModelContext(ConnectionContext connectionContext, CancellationToken createCancellationToken)
            {
                var modelContext = await connectionContext.CreateModelContext(createCancellationToken).ConfigureAwait(false);

                LogContext.Debug?.Log("Created model: {ChannelNumber} {Host}", modelContext.Model.ChannelNumber, connectionContext.Description);

                return modelContext;
            }

            return _supervisor.CreateAgent(asyncContext, CreateModelContext, cancellationToken);
        }
    }
}
