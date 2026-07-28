namespace InovaGed.Infrastructure.Continuity;
internal static class BackupJobStatuses
{
 public const string Pending="PENDING", Claimed="CLAIMED", Running="RUNNING", Verifying="VERIFYING", Completed="COMPLETED", Retry="RETRY", Failed="FAILED", DeadLetter="DEAD_LETTER", Cancelled="CANCELLED";
 public static readonly string[] Terminal=[Completed,Failed,DeadLetter,Cancelled];
 public static bool CanTransition(string from,string to)=>from switch{Pending=>to is Claimed or Cancelled,Retry=>to is Claimed or Cancelled,Claimed=>to is Running or Retry or DeadLetter or Cancelled,Running=>to is Verifying or Completed or Retry or DeadLetter or Cancelled,Verifying=>to is Completed or Retry or DeadLetter or Cancelled,_=>false};
}
