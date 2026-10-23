# Chatwoot setup for the office (TASK 07b, DECISIONS T-24). Run by scripts/*/chatwoot-setup with
# `rails runner`; safe to run again (it finds what exists and only fixes tokens and settings).
#
#   - the office's account (Arabic);
#   - «حازم (النظام)», an administrator nobody signs in as: .NET writes the client's messages and
#     Hazem's replies with its token (CHATWOOT_BOT_TOKEN), and ignores what it wrote itself;
#   - a Platform App (CHATWOOT_PLATFORM_TOKEN): .NET creates, disables and re-roles staff accounts;
#   - the API inbox «واتساب (حازم)»: no WhatsApp, no Meta. Its webhook calls the api on chatwoot_net,
#     signed with CHATWOOT_WEBHOOK_TOKEN (X-Chatwoot-Signature). Auto-assignment off: the office assigns;
#   - Chatwoot's first-run page closed: otherwise anyone who opens Chatwoot could make a super admin.
# Prints one JSON line with the ids; never a token.
require 'json'
require 'securerandom'

def need(name)
  value = ENV.fetch(name, '')
  abort("#{name} is empty") if value.empty?
  value
end

account_name = 'مكتب سعد فتحي سعد'
inbox_name = 'واتساب (حازم)'
hazem_email = 'hazem@office.invalid'
webhook = 'http://api:8080/chatwoot/webhook'
secret = need('CHATWOOT_WEBHOOK_TOKEN')

ActiveRecord::Base.transaction do
  account = Account.find_by(name: account_name) || Account.create!(name: account_name, locale: 'ar')
  account.update!(locale: 'ar') unless account.locale == 'ar'

  hazem = User.find_by(email: hazem_email)
  unless hazem
    hazem = User.new(name: 'حازم (النظام)', display_name: 'حازم', email: hazem_email,
                     password: "#{SecureRandom.base58(40)}aA1!")
    hazem.skip_confirmation!
    hazem.save!
  end
  AccountUser.find_or_create_by!(account: account, user: hazem) { |au| au.role = :administrator }
  hazem.access_token.update!(token: need('CHATWOOT_BOT_TOKEN'))

  app = PlatformApp.find_by(name: 'office-api') || PlatformApp.create!(name: 'office-api')
  app.access_token.update!(token: need('CHATWOOT_PLATFORM_TOKEN'))
  PlatformAppPermissible.find_or_create_by!(platform_app: app, permissible: account)
  PlatformAppPermissible.find_or_create_by!(platform_app: app, permissible: hazem)

  inbox = account.inboxes.find_by(name: inbox_name)
  if inbox
    inbox.channel.update!(webhook_url: webhook, secret: secret)
  else
    channel = Channel::Api.create!(account: account, webhook_url: webhook, secret: secret)
    inbox = Inbox.create!(account: account, name: inbox_name, channel: channel)
  end
  inbox.update!(enable_auto_assignment: false) if inbox.enable_auto_assignment

  ::Redis::Alfred.delete(::Redis::Alfred::CHATWOOT_INSTALLATION_ONBOARDING)

  puts({ account_id: account.id, inbox_id: inbox.id, inbox_identifier: inbox.channel.identifier,
         hazem_user_id: hazem.id, platform_app_id: app.id }.to_json)
end
