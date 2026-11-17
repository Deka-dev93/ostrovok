namespace Ostrovok;

public class Spring
{
    public double Value, Velocity, Target;
    public double Stiffness = 260, Damping = 20, Mass = 1;

    public Spring(double v = 0) { Value = Target = v; }

    public bool Settled => Math.Abs(Target - Value) < 0.05 && Math.Abs(Velocity) < 0.05;

    public void Step(double dt)
    {
        dt = Math.Min(dt, 1.0 / 30);
    
        int n = 8; double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double a = (-Stiffness * (Value - Target) - Damping * Velocity) / Mass;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (Settled) { Value = Target; Velocity = 0; }
    }
}
