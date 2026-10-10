using System;
using PersistentFlight;
class SoiPlanTests {
    static int checks;static string reason;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static SoiPlanDecision Run(int body,double ut,double dv,double error,double duration){return SoiPlanPolicy.Check(1,2,body,100,ut,100,dv,error,duration,out reason);}
    static void Main(){try {
        Check(Run(1,1000,0,0,0)==SoiPlanDecision.Wait,"Departure must wait");
        Check(Run(3,1000,100,0,5)==SoiPlanDecision.Hold,"Unexpected body must hold");
        Check(Run(2,1000,105,100,5)==SoiPlanDecision.Ready,"Correct arrival rejected");
        Check(Run(2,1000,151,0,5)==SoiPlanDecision.Hold,"Excess correction accepted");
        Check(Run(2,1000,150,20000,5)==SoiPlanDecision.Ready,"Boundary correction rejected");
        Check(Run(2,1000,100,20001,5)==SoiPlanDecision.Hold,"Wrong arrival position accepted");
        Check(Run(2,164,100,0,5)==SoiPlanDecision.Hold,"Insufficient alignment time accepted");
        Check(Run(2,165,100,0,5)==SoiPlanDecision.Ready,"Alignment boundary rejected");
        Check(Run(1,105,0,0,0)==SoiPlanDecision.Hold,"Missed encounter deadline ignored");
        foreach(double bad in new[]{Double.NaN,Double.PositiveInfinity,-1.0}){
            Check(Run(2,1000,bad,0,5)==SoiPlanDecision.Hold,"Invalid residual accepted");
            Check(Run(2,1000,100,bad,5)==SoiPlanDecision.Hold,"Invalid position accepted");
            Check(Run(2,1000,100,0,bad)==SoiPlanDecision.Hold,"Invalid duration accepted");
        }
        var random=new Random(817);
        for(int i=0;i<10000;i++){
            double duration=random.NextDouble()*60,remaining=duration+60+random.NextDouble()*1000;
            Check(Run(2,100+remaining,50+random.NextDouble()*100,random.NextDouble()*20000,duration)==SoiPlanDecision.Ready,"Valid randomized arrival rejected");
            Check(Run(3,100+remaining,100,0,duration)==SoiPlanDecision.Hold,"Randomized wrong body accepted");
        }
        Console.WriteLine("PASS "+checks+" SOI plan checks");
    }catch(Exception e){Console.Error.WriteLine("FAIL "+e);Environment.Exit(1);}}
}
