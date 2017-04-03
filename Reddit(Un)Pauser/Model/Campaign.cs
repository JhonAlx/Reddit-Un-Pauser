namespace Reddit_Un_Pauser.Model
{
    public enum State
    {
        Running,
        Paused
    }

    public class Campaign
    {
        public string CampaignID;
        public string PromotionID;
        public string Uh;
        public State State;
    }
}