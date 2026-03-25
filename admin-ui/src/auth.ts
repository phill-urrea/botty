import NextAuth from "next-auth"
import Google from "next-auth/providers/google"

export const { handlers, signIn, signOut, auth } = NextAuth({
  providers: [Google],
  pages: {
    signIn: "/auth/signin",
    error: "/auth/error",
  },
  callbacks: {
    signIn({ user }) {
      const allowedEmail = process.env.ALLOWED_EMAIL;
      if (allowedEmail && user.email !== allowedEmail) {
        return false;
      }
      return true;
    },
    authorized({ auth }) {
      if (!process.env.AUTH_SECRET) return true;
      return !!auth?.user;
    },
  },
})
