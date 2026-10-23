namespace RonSijm.RepoGen.Tool.Tests;

/// <summary>Production markers intentionally cannot execute: generated fixtures must replace exact calls.</summary>
public static class TranslationCalls
{
    public static int Measure(string value) => throw new InvalidOperationException("Production helper was executed.");
    public static int Shift(int value, int amount) => throw new InvalidOperationException("Production helper was executed.");
    public static T Echo<T>(T value) => throw new InvalidOperationException("Production helper was executed.");
    public static bool Positive(int value) => throw new InvalidOperationException("Production helper was executed.");
    public static int Unit(int value, string unit) => throw new InvalidOperationException("Production helper was executed.");
    public static int Three(int a, int b, int c) => throw new InvalidOperationException("Production helper was executed.");
    public static int Four(int a, int b, int c, int d) => throw new InvalidOperationException("Production helper was executed.");
}
