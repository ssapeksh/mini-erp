export interface UserInfo {
  id: number;
  userName: string;
  fullName: string;
  role: string;
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresOn: string;
  refreshToken: string;
  refreshTokenExpiresOn: string;
  sessionId: string;
  user: UserInfo;
}
