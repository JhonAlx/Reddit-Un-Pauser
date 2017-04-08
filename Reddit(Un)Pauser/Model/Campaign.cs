namespace Reddit_Un_Pauser.Model
{
    public enum State
    {
        Running,
        Paused
    }

    public class Campaign
    {
        public string CampaignId;
        public string PromotionId;
        public string Uh;
        public State State;
    }
}