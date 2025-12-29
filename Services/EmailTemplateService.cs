using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace HikingStore.Services
{
    public interface IEmailTemplateService
    {
        string GenerateEmailConfirmationTemplate(string callbackUrl, string userName);
        string GenerateWelcomeEmailTemplate(string userName);
    }

    public class EmailTemplateService : IEmailTemplateService
    {
        public string GenerateEmailConfirmationTemplate(string callbackUrl, string userName)
        {
            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Confirm Your Email</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            margin: 0;
            padding: 0;
            background-color: #f8f9fa;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #2d473d 0%, #3b5d50 100%);
            color: white;
            padding: 40px 20px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 28px;
            font-weight: 700;
        }}
        .content {{
            padding: 40px 30px;
        }}
        .welcome-text {{
            font-size: 18px;
            color: #2d473d;
            margin-bottom: 20px;
        }}
        .confirmation-box {{
            background: #f8f9fa;
            border-radius: 8px;
            padding: 25px;
            margin: 25px 0;
            text-align: center;
            border-left: 4px solid #3b5d50;
        }}
        .btn-confirm {{
            display: inline-block;
            background: linear-gradient(135deg, #3b5d50, #2d473d);
            color: white;
            padding: 14px 35px;
            text-decoration: none;
            border-radius: 8px;
            font-weight: 600;
            font-size: 16px;
            margin: 15px 0;
            transition: all 0.3s ease;
        }}
        .btn-confirm:hover {{
            background: linear-gradient(135deg, #2d473d, #1a2f26);
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(59, 93, 80, 0.3);
        }}
        .security-note {{
            background: #fff3cd;
            border: 1px solid #ffeaa7;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
            font-size: 14px;
            color: #856404;
        }}
        .footer {{
            background: #f8f9fa;
            padding: 25px;
            text-align: center;
            border-top: 1px solid #e9ecef;
            color: #6c757d;
            font-size: 14px;
        }}
        .logo {{
            font-size: 24px;
            font-weight: 700;
            color: #3b5d50;
            margin-bottom: 10px;
        }}
        .steps {{
            margin: 25px 0;
        }}
        .step {{
            display: flex;
            align-items: center;
            margin-bottom: 15px;
            padding: 15px;
            background: #f8f9fa;
            border-radius: 8px;
        }}
        .step-number {{
            background: #3b5d50;
            color: white;
            width: 30px;
            height: 30px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            font-weight: bold;
            margin-right: 15px;
            flex-shrink: 0;
        }}
        .alternative-link {{
            word-break: break-all;
            color: #6c757d;
            font-size: 14px;
            margin-top: 10px;
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>
            <h1>🎉 Welcome to HikingStore!</h1>
            <p>Your Adventure Awaits</p>
        </div>
        
        <div class='content'>
            <div class='welcome-text'>
                Hi <strong>{userName}</strong>,
            </div>
            
            <p>Thank you for joining HikingStore! We're excited to have you as part of our community of outdoor enthusiasts.</p>
            
            <div class='confirmation-box'>
                <h3>🔐 Confirm Your Email Address</h3>
                <p>To complete your registration and start exploring our premium hiking gear, please confirm your email address by clicking the button below:</p>
                
                <a href='{callbackUrl}' class='btn-confirm'>Confirm Email Address</a>
                
                <div class='alternative-link'>
                    Or copy and paste this link in your browser:<br>
                    <a href='{callbackUrl}'>{callbackUrl}</a>
                </div>
            </div>

            <div class='steps'>
                <div class='step'>
                    <div class='step-number'>1</div>
                    <div>Click the confirmation button above</div>
                </div>
                <div class='step'>
                    <div class='step-number'>2</div>
                    <div>Verify your email address</div>
                </div>
                <div class='step'>
                    <div class='step-number'>3</div>
                    <div>Start exploring premium hiking gear</div>
                </div>
            </div>

            <div class='security-note'>
                <strong>🔒 Security Note:</strong> This link will expire in 24 hours for your security. If you didn't create an account with HikingStore, please ignore this email.
            </div>

            <p><strong>What's Next?</strong><br>
            After confirming your email, you'll have access to:</p>
            <ul>
                <li>🏔️ Premium hiking equipment and gear</li>
                <li>🚚 Fast and free shipping on orders over $50</li>
                <li>⭐ Exclusive member discounts and offers</li>
                <li>📦 Easy order tracking and management</li>
            </ul>

            <p>Happy trails!<br>
            <strong>The HikingStore Team</strong></p>
        </div>
        
        <div class='footer'>
            <div class='logo'>HikingStore</div>
            <p>Your trusted partner for premium outdoor gear</p>
            <p>
                📧 support@hikingstore.com | 
                📞 +1 (555) 123-HELP | 
                🏔️ www.hikingstore.com
            </p>
            <p style='margin-top: 15px; font-size: 12px; color: #adb5bd;'>
                &copy; 2024 HikingStore. All rights reserved.<br>
                This email was sent to you because you registered an account with HikingStore.
            </p>
        </div>
    </div>
</body>
</html>";
        }

        public string GenerateWelcomeEmailTemplate(string userName)
        {
            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Welcome to HikingStore</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            margin: 0;
            padding: 0;
            background-color: #f8f9fa;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #2d473d 0%, #3b5d50 100%);
            color: white;
            padding: 40px 20px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 28px;
            font-weight: 700;
        }}
        .content {{
            padding: 40px 30px;
        }}
        .welcome-text {{
            font-size: 18px;
            color: #2d473d;
            margin-bottom: 20px;
        }}
        .btn-primary {{
            display: inline-block;
            background: linear-gradient(135deg, #3b5d50, #2d473d);
            color: white;
            padding: 14px 35px;
            text-decoration: none;
            border-radius: 8px;
            font-weight: 600;
            font-size: 16px;
            margin: 15px 0;
            transition: all 0.3s ease;
        }}
        .btn-primary:hover {{
            background: linear-gradient(135deg, #2d473d, #1a2f26);
            transform: translateY(-2px);
        }}
        .features {{
            margin: 30px 0;
        }}
        .feature {{
            display: flex;
            align-items: center;
            margin-bottom: 15px;
            padding: 15px;
            background: #f8f9fa;
            border-radius: 8px;
        }}
        .feature-icon {{
            background: #3b5d50;
            color: white;
            width: 40px;
            height: 40px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            margin-right: 15px;
            flex-shrink: 0;
        }}
        .footer {{
            background: #f8f9fa;
            padding: 25px;
            text-align: center;
            border-top: 1px solid #e9ecef;
            color: #6c757d;
            font-size: 14px;
        }}
        .logo {{
            font-size: 24px;
            font-weight: 700;
            color: #3b5d50;
            margin-bottom: 10px;
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>
            <h1>🌟 Welcome to HikingStore!</h1>
            <p>Your Adventure Starts Now</p>
        </div>
        
        <div class='content'>
            <div class='welcome-text'>
                Hi <strong>{userName}</strong>,
            </div>
            
            <p>Welcome to the HikingStore family! We're thrilled to have you on board and can't wait to help you gear up for your next outdoor adventure. 🏔️</p>

            <div style='text-align: center; margin: 30px 0;'>
                <a href='https://yourwebsite.com/Home/Product' class='btn-primary'>Start Exploring Gear</a>
            </div>

            <div class='features'>
                <h3 style='color: #2d473d; margin-bottom: 20px;'>Here's what you can enjoy as a member:</h3>
                
                <div class='feature'>
                    <div class='feature-icon'>🏔️</div>
                    <div>
                        <strong>Premium Hiking Gear</strong><br>
                        <small>Top-quality equipment from trusted brands</small>
                    </div>
                </div>
                
                <div class='feature'>
                    <div class='feature-icon'>🚚</div>
                    <div>
                        <strong>Free Shipping</strong><br>
                        <small>On all orders over $50</small>
                    </div>
                </div>
                
                <div class='feature'>
                    <div class='feature-icon'>⭐</div>
                    <div>
                        <strong>Exclusive Discounts</strong><br>
                        <small>Member-only offers and early access to sales</small>
                    </div>
                </div>
                
                <div class='feature'>
                    <div class='feature-icon'>📦</div>
                    <div>
                        <strong>Easy Returns</strong><br>
                        <small>30-day hassle-free return policy</small>
                    </div>
                </div>
            </div>

            <div style='background: #e8f5e8; border: 1px solid #c8e6c9; border-radius: 8px; padding: 20px; margin: 25px 0;'>
                <h4 style='color: #2d473d; margin-top: 0;'>🎁 Special Welcome Offer</h4>
                <p style='margin-bottom: 0;'>Use code <strong>WELCOME10</strong> at checkout to get 10% off your first order!</p>
            </div>

            <p>Ready to explore? Check out our latest collections and find the perfect gear for your next adventure.</p>

            <p>Happy trails!<br>
            <strong>The HikingStore Team</strong></p>
        </div>
        
        <div class='footer'>
            <div class='logo'>HikingStore</div>
            <p>Your trusted partner for premium outdoor gear</p>
            <p>
                📧 support@hikingstore.com | 
                📞 +383 45-804-555HELP | 
                🏔️ www.hikingstore.com
            </p>
            <p style='margin-top: 15px; font-size: 12px; color: #adb5bd;'>
                &copy; 2024 HikingStore. All rights reserved.<br>
                Adventure awaits - gear up with confidence!
            </p>
        </div>
    </div>
</body>
</html>";
        }
    }
}
