using System;
using RLBotDotNet;
using rlbot.flat;
using System.Diagnostics;

namespace RLBotCSharpExample
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class ExampleBot : Bot
    {
        // We want the constructor for ExampleBot to extend from Bot, but we don't want to add anything to it.
        public ExampleBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) { }

        /* private static double Stopwatch()
         {
             
             var TimeElapsed = s.Elapsed.Milliseconds;
             return TimeElapsed;
         } */

        private static Stopwatch s = new Stopwatch();

        public override Controller GetOutput(GameTickPacket gameTickPacket)
        {
            // This controller object will be returned at the end of the method.
            // This controller will contain all the inputs that we want the bot to perform.
            Controller controller = new Controller();

            // Wrap gameTickPacket retrieving in a try-catch so that the bot doesn't crash whenever a value isn't present.
            // A value may not be present if it was not sent.
            // These are nullables so trying to get them when they're null will cause errors, therefore we wrap in try-catch.
            try
            {
                // Store the required data from the gameTickPacket.
                Vector3 ballLocation = gameTickPacket.Ball.Value.Physics.Value.Location.Value;
                Vector3 carLocation = gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value;
                Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;

                // Calcluate the distance from the car to the ball
                var distanceToBall = Get2DDistance(carLocation.X, ballLocation.X, carLocation.Y, carLocation.Y);

                // Calculate to get the angle from the front of the bot's car to the ball.
                double botToTargetAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
                double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
                
                // Decide which way to steer in order to get to the ball.
                float steer = (float)(botFrontToTargetAngle / Math.PI) * 3f;
                controller.Steer = steer;
                controller.Handbrake = (Math.Abs(steer) > 0.87);

                // Kickoff
                if (ballLocation.X == 0 && ballLocation.Y == 0)
                {
                    // Left Corner Spawn
                    if ((int)carLocation.X == 2043 && (int)carLocation.Y == -2555)
                    {
                        // DistanceToBall = 3271 
                        
                    }
                    // Right Corner Spawn 
                    else if ((int)carLocation.X == -2043 && (int)carLocation.Y == -2555)
                    {
                        // DistanceToBall = 3271

                    }
                    // Back Left Spawn
                    else if ((int)carLocation.X == 256 && (int)carLocation.Y == -3833)
                    {
                        // DistanceToBall = 3842
                        
                    }
                    // Back Right Spawn
                    else if ((int)Math.Round(carLocation.X) == -256 && (int)Math.Round(carLocation.Y) == -3833)
                    {
                        // DistanceToBall = 3842
                        
                    }
                    // Far Back Center Spawn
                    else if ((int)carLocation.X == 0 && (int)carLocation.Y == -4601)
                    {
                        // DistanceToBall = 4601

                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }

            // Set the throttle to 1 so the bot can move.
            controller.Throttle = 1;
            return controller;
        }
        private static double CorrectAngle(double botFrontToTargetAngle)
        {
            // Correct the angle
            if (botFrontToTargetAngle < -Math.PI)
                botFrontToTargetAngle += 2 * Math.PI;
            if (botFrontToTargetAngle > Math.PI)
                botFrontToTargetAngle -= 2 * Math.PI;
            return botFrontToTargetAngle;
        }
        public double Get2DDistance(double x1, double x2, double y1, double y2)
        {
            return Math.Sqrt(Math.Pow((x2 - x1), 2) + Math.Pow((y2 - y1), 2));
        }
    }
}
