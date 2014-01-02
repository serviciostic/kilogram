﻿using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage.FileProperties;
using Telegram.Annotations;
using Telegram.Core.Logging;
using Telegram.MTProto;
using Telegram.MTProto.Exceptions;
using Telegram.UI;

namespace Telegram.Model.Wrappers {

    public delegate void UserModelChangeHandler();
    public class UserModel : INotifyPropertyChanged {
        private static readonly Logger logger = LoggerFactory.getLogger(typeof(StartPage));

        private User user;

        public event UserModelChangeHandler ChangeEvent;

        public UserModel(User user) {
            this.user = user;
        }

        public void SetUser(User user) {
            this.user = user;
            ChangeEvent();
            OnPropertyChanged("FullName");
            OnPropertyChanged("Status");
        }

        public int Id {
            get {
                switch (user.Constructor) {
                    case Constructor.userEmpty:
                        return ((UserEmptyConstructor)user).id;
                    case Constructor.userSelf:
                        return ((UserSelfConstructor) user).id;
                    case Constructor.userContact:
                        return ((UserContactConstructor) user).id;
                    case Constructor.userRequest:
                        return ((UserRequestConstructor) user).id;
                    case Constructor.userForeign:
                        return ((UserForeignConstructor) user).id;
                    case Constructor.userDeleted:
                        return ((UserDeletedConstructor) user).id;
                    default:
                        throw new InvalidDataException("invalid constructor");
                }
            }
        }

        public User RawUser {
            get {
                return user;
            }
        }

        public string FullName {
            get {
                switch (user.Constructor) {
                    case Constructor.userEmpty:
                        return "empty";
                    case Constructor.userSelf:
                        return ((UserSelfConstructor)user).first_name + " " + ((UserSelfConstructor)user).first_name;
                    case Constructor.userContact:
                        return ((UserContactConstructor)user).first_name + " " + ((UserContactConstructor)user).last_name;
                    case Constructor.userRequest:
                        return ((UserRequestConstructor)user).first_name + " " + ((UserRequestConstructor)user).last_name;
                    case Constructor.userForeign:
                        return ((UserForeignConstructor)user).first_name + " " + ((UserForeignConstructor)user).last_name;
                    case Constructor.userDeleted:
                        return ((UserDeletedConstructor)user).first_name + " " + ((UserDeletedConstructor)user).last_name;
                    default:
                        throw new InvalidDataException("invalid constructor");
                }
            }
        }

        public string Status {
            get {
                switch (user.Constructor) {
                    case Constructor.userEmpty:
                        return "loading";
                    case Constructor.userSelf:
                        return GetStatusString(((UserSelfConstructor) user).status);
                    case Constructor.userContact:
                        return GetStatusString(((UserContactConstructor)user).status);
                    case Constructor.userRequest:
                        return GetStatusString(((UserRequestConstructor) user).status);
                    case Constructor.userForeign:
                        return GetStatusString(((UserForeignConstructor) user).status);
                    case Constructor.userDeleted:
                        return "deleted";
                    default:
                        throw new InvalidDataException("invalid constructor");
                }
            }
        }

        public static string GetStatusString(UserStatus status) {
            string statusString = "unknown";
            switch (status.Constructor) {
                case Constructor.userStatusEmpty:
                    statusString = "loading";
                    break;
                case Constructor.userStatusOffline:
                    statusString = "offline";
                    break;
                case Constructor.userStatusOnline:
                    statusString = "online";
                    break;
            }

            return statusString;
        }

        public static string GetLastOnlineTime(int lastOnline) {
            return "last online 14:88a";
        }


        private PeerNotifySettingsConstructor peerNotifySettings = null;

        private void UpdatePeerNotifySettings(PeerNotifySettingsConstructor newSettings) {
            if (newSettings == null) {
                logger.error("Strange shit is happened, newSettings == null");
                return;
            }

            if (peerNotifySettings == null) {
                peerNotifySettings = newSettings;
                OnPropertyChanged("NotificationSound");
                OnPropertyChanged("NotificationsEnabled");
                return;
            }

            if (peerNotifySettings.sound != newSettings.sound) {
                peerNotifySettings = newSettings;
                OnPropertyChanged("NotificationSound");
            } else if (peerNotifySettings.mute_until != newSettings.mute_until) {
                peerNotifySettings = newSettings;
                OnPropertyChanged("NotificationsEnabled");
            }
        }

        public string NotificationSound {
            get {
                GetUserSettings();
                return peerNotifySettings == null ? "Default" : peerNotifySettings.sound;
            }
            set {
                peerNotifySettings.sound = value;
                UpdateUserSettings();
                OnPropertyChanged("NotificationSound");
            }
        }

        public InputPeer InputPeer {
            get {
                switch (user.Constructor) {
                    case Constructor.userEmpty:
                        return TL.inputPeerEmpty();
                    case Constructor.userSelf:
                        return TL.inputPeerSelf();
                    case Constructor.userContact:
                        return TL.inputPeerContact(((UserContactConstructor) user).id);
                    case Constructor.userRequest:
                        return TL.inputPeerForeign(((UserRequestConstructor)user).id, ((UserRequestConstructor)user).access_hash);
                    case Constructor.userForeign:
                        return TL.inputPeerForeign(((UserForeignConstructor)user).id, ((UserForeignConstructor)user).access_hash);
                    case Constructor.userDeleted:
                        return TL.inputPeerEmpty();
                    default:
                        throw new InvalidDataException("invalid constructor");
                }
            }
        }

        public bool NotificationsEnabled {
            get {
                GetUserSettings();

                if(peerNotifySettings == null)
                    return true;
                
                DateTime nowTime = DateTime.Now;
                DateTime muteUntil = DateTimeExtensions.DateTimeFromUnixTimestampSeconds(peerNotifySettings.mute_until);

                if (nowTime > muteUntil)
                    return true;

                return false;
            }
            set {
                logger.debug("NotificationsEnabled " + value);
                
                if (peerNotifySettings == null) {
                    GetUserSettings();
                    return;
                }

                if (value == false)
                    peerNotifySettings.mute_until = int.MaxValue;
                else
                    peerNotifySettings.mute_until = 0;

                UpdateUserSettings();
                OnPropertyChanged("NotificationsEnabled");
            }
        }

        private bool _updateInProgress = false;

        private async Task UpdateUserSettings() {
            if (_updateInProgress)
                return;

            // LOCK ON THIS
            _updateInProgress = true;
            
            logger.debug("Synchronizing settings");
            try {
                InputPeerNotifySettings newSettings = TL.inputPeerNotifySettings(peerNotifySettings.mute_until,
                    peerNotifySettings.sound, peerNotifySettings.show_previews, peerNotifySettings.events_mask);

                // FIXME: catch exception, process BOOl error
                bool update = await TelegramSession.Instance.Api.account_updateNotifySettings(TL.inputNotifyPeer
                    (TL.inputPeerContact(Id)), newSettings);

                logger.debug("Synchronized settings: " + update);
            }
            catch (MTProtoException ex) {
                logger.error("UpdateUserSettings Exception");
            }
            // UNLOCK ON THIS
            _updateInProgress = false;

        }

        private volatile bool _getInProgress = false;

        private async Task GetUserSettings() {
            if (_getInProgress)
                return;

            _getInProgress = true;
            // FIXME: catch exception

            await TelegramSession.Instance.Established;
            try {
                PeerNotifySettings settings =
                    await
                        TelegramSession.Instance.Api.account_getNotifySettings(
                            TL.inputNotifyPeer(TL.inputPeerContact(Id)));
            

            switch (settings.Constructor) {
                case Constructor.peerNotifySettings:
                    UpdatePeerNotifySettings(settings as PeerNotifySettingsConstructor);
                    break;

                case Constructor.peerNotifySettingsEmpty:
                    logger.error("Unable to get USER settings: Constructor.peerNotifySettingsEmpty");
                    break;
            }

            } catch (MTProtoException ex) {
                logger.error("GetUserSettings Exception");
            }

            _getInProgress = false;
        }



        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
