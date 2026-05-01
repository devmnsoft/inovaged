namespace InovaGed.Infrastructure.Users
{
    public sealed class ServidorUsuarioCreatedDto
    {
        public Guid ServidorId { get; set; }
        public Guid? UserId { get; set; }
    }
}
