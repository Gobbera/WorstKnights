using System;

public abstract class BeholderAttackExecution : IDisposable
{
    protected BeholderAttackContext Context { get; }
    public bool IsFinished { get; protected set; }
    protected BeholderAttackExecution(BeholderAttackContext context) { Context = context; }
    public abstract void Begin();
    public virtual void Tick(float deltaTime) { }
    public virtual void Cancel() { IsFinished = true; }
    public virtual void Dispose() { }
}
