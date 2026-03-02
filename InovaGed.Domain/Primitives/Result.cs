namespace InovaGed.Domain.Primitives
{
    public record Error(string Code, string Message);

    public class Result
    {
        public bool Success { get; }
        public bool IsFailure => !Success;
        public Error? Error { get; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }

        protected Result(bool success, Error? error)
        {
            Success = success;
            Error = error;
        }

        public static Result Ok() => new(true, null);

        public static Result Fail(string code, string message) =>
            new(false, new Error(code, message));
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        protected Result(bool success, Error? error, T? value)
            : base(success, error)
        {
            Value = value;
        }

        public static Result<T> Ok(T value) =>
            new(true, null, value);

        public static Result<T> Fail(string code, string message) =>
            new(false, new Error(code, message), default);
    }
}
