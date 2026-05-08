namespace InovaGed.Domain.Primitives
{
    public record Error(string Code, string Message);

    public class Result
    {
        public bool Success { get; }
        public bool IsSuccess { get; }
        public bool IsFailure => !Success;
        public Error? Error { get; }
        public string ErrorMessage { get; }

        protected Result(bool success, Error? error)
        {
            Success = success;
            IsSuccess = success;
            Error = error;
            ErrorMessage = error?.Message ?? string.Empty;
        }

        public static Result Ok()
        {
            return new Result(true, null);
        }

        public static Result Fail(string code, string message)
        {
            return new Result(false, new Error(code, message));
        }
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        protected Result(bool success, Error? error, T? value)
            : base(success, error)
        {
            Value = value;
        }

        public static Result<T> Ok(T value)
        {
            return new Result<T>(true, null, value);
        }

        public static Result<T> Fail(string code, string message)
        {
            return new Result<T>(false, new Error(code, message), default);
        }
    }
}